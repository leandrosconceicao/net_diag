using NetDiag.Core.Abstractions;
using NetDiag.Core.Checks;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Rules;

/// <summary>
/// Regras que cruzam as medições das camadas e apontam a causa provável.
/// A lógica central: comparar camadas vizinhas. Se o roteador está bem e a internet não,
/// o problema está depois do roteador (provedor); se o roteador já está mal, é local.
/// </summary>
public sealed class BuiltInRules : IDiagnosticRule
{
    private const string AreaPc = "Seu computador";
    private const string AreaWifi = "Wi-Fi";
    private const string AreaLan = "Rede local / roteador";
    private const string AreaIsp = "Provedor de internet";
    private const string AreaDns = "DNS";
    private const string AreaSpeed = "Velocidade";

    public IEnumerable<Finding> Evaluate(RuleContext c)
    {
        var list = new List<Finding>();

        // Sem conexão básica, as demais análises não fazem sentido.
        if (Connectivity(c, list)) return list;

        LocalNetwork(c, list);
        Wifi(c, list);
        EquipmentLimit(c, list);
        Internet(c, list);
        Dns(c, list);
        Route(c, list);
        Speed(c, list);
        Environment(c, list);
        return list;
    }

    /// <returns>true se encontrou um problema que impede qualquer outra análise.</returns>
    private static bool Connectivity(RuleContext c, List<Finding> list)
    {
        var a = c.Adapter;
        if (a is null) return false;

        if (a.Has("no_interface"))
        {
            list.Add(new Finding("conn.no_interface", Severity.Critical, AreaPc,
                "O computador não está conectado a nenhuma rede",
                "Nenhum adaptador de rede (cabo ou Wi-Fi) está ativo.",
                "Verifique se o cabo está bem encaixado nas duas pontas, ou se o Wi-Fi está ligado e conectado " +
                "à sua rede. Confira também se o modo avião está desativado.",
                Confidence.Alta));
            return true;
        }

        if (a.Has("apipa"))
        {
            list.Add(new Finding("conn.apipa", Severity.Critical, AreaLan,
                "O roteador não entregou um endereço ao computador",
                "O computador está ligado à rede, mas não recebeu a configuração automática (DHCP). " +
                "Sem ela, não consegue navegar.",
                "Desligue o roteador da tomada por 30 segundos e ligue de novo. Depois, desconecte e " +
                "reconecte o Wi-Fi ou o cabo. Em redes de empresa, avise o suporte de TI.",
                Confidence.Alta) { FixId = "renew_ip" });
            return true;
        }

        if (a.Has("no_gateway"))
        {
            list.Add(new Finding("conn.no_gateway", Severity.Critical, AreaLan,
                "Conectado à rede, mas sem roteador configurado",
                "O adaptador está ativo, mas não sabe por onde chegar à internet (falta o gateway).",
                "Reinicie o roteador e reconecte. Se você configurou o IP manualmente, volte para " +
                "\"Obter endereço automaticamente\".",
                Confidence.Alta) { FixId = "renew_ip" });
            return true;
        }

        return false;
    }

    private static void LocalNetwork(RuleContext c, List<Finding> list)
    {
        var g = c.Gateway;
        var inet = c.Internet;
        var signal = c.Wifi?.Get("signal");

        if (g?.Has("gw_unreachable") == true)
        {
            if (inet is not null && !inet.Has("offline") && RuleContext.Ran(inet))
            {
                list.Add(new Finding("lan.gw_no_ping", Severity.Info, AreaLan,
                    "O roteador não responde a ping",
                    "A internet funciona, mas o roteador ignora pings. Alguns modelos fazem isso de propósito.",
                    "", Confidence.Media));
            }
            else
            {
                list.Add(new Finding("lan.gw_down", Severity.Critical, AreaLan,
                    "O roteador não está respondendo",
                    "O computador não consegue se comunicar com o roteador da sua casa ou empresa.",
                    "Desligue o roteador da tomada por 30 segundos e ligue de novo. Verifique os cabos. " +
                    (c.IsWireless ? "Se possível, aproxime-se do roteador." : ""),
                    Confidence.Alta));
            }
        }
        else if (g?.Has("gw_unstable") == true)
        {
            var loss = g.Get("gw_loss") ?? 0;
            var avg = g.Get("gw_avg") ?? 0;
            var numbers = $"Até o roteador: média de {avg:0} ms e {loss:0}% de pacotes perdidos " +
                          $"(o normal é abaixo de {(c.IsWireless ? GatewayCheck.WirelessLimitMs : GatewayCheck.WiredLimitMs):0} ms e 0% de perda).";

            if (c.IsWireless)
            {
                list.Add(new Finding("lan.wifi_unstable", Severity.Warning, AreaWifi,
                    "Conexão instável entre o computador e o roteador",
                    "O problema está dentro de casa, antes da internet. " + numbers,
                    "Aproxime-se do roteador ou tire obstáculos (paredes, espelhos, micro-ondas) do caminho. " +
                    "Prefira a rede 5 GHz. Para PCs fixos, o cabo de rede resolve de vez.",
                    (signal is < 50) ? Confidence.Alta : Confidence.Media));
            }
            else
            {
                list.Add(new Finding("lan.wired_unstable", Severity.Warning, AreaLan,
                    "Conexão instável entre o computador e o roteador",
                    "Mesmo com cabo, a comunicação com o roteador está falhando. " + numbers,
                    "Troque o cabo de rede, teste outra porta do roteador e reinicie o roteador. " +
                    "Se persistir, o roteador pode estar sobrecarregado ou com defeito.",
                    Confidence.Media));
            }
        }

        var errorRate = c.Adapter?.Get("error_rate");
        if (errorRate > 1)
        {
            list.Add(new Finding("pc.nic_errors", Severity.Warning, AreaPc,
                "Muitos pacotes com erro no adaptador de rede",
                $"{errorRate:0.#}% dos pacotes tiveram erro. Isso costuma indicar cabo danificado ou driver com problema.",
                "Troque o cabo de rede e atualize o driver do adaptador pelo site do fabricante.",
                Confidence.Media));
        }

        if (c.Capability?.Has("half_duplex") == true)
        {
            list.Add(new Finding("pc.half_duplex", Severity.Warning, AreaPc,
                "A placa de rede está em modo half-duplex",
                "Nesse modo a placa não consegue enviar e receber ao mesmo tempo. Isso reduz a velocidade " +
                "e causa lentidão e erros, principalmente em chamadas de vídeo.",
                "No Gerenciador de Dispositivos, abra a placa de rede > aba Avançado > \"Speed & Duplex\" e " +
                "escolha \"Auto Negotiation\". Se continuar, troque o cabo.",
                Confidence.Alta) { FixId = "reset_speed_duplex" });
        }
    }

    private static void Wifi(RuleContext c, List<Finding> list)
    {
        var w = c.Wifi;
        if (w is null) return;

        if (w.Has("location_blocked"))
        {
            list.Add(new Finding("wifi.location_blocked", Severity.Info, AreaWifi,
                "Não foi possível ler os detalhes do Wi-Fi",
                "O Windows exige permissão de localização para mostrar sinal e canal do Wi-Fi.",
                "Configurações > Privacidade e segurança > Localização: ative a localização e permita que " +
                "aplicativos da área de trabalho a acessem. Depois rode o diagnóstico de novo.",
                Confidence.Alta));
            return;
        }
        if (!RuleContext.Ran(w)) return;

        var signal = w.Get("signal");
        if (signal is < 40)
        {
            list.Add(new Finding("wifi.weak", Severity.Warning, AreaWifi,
                $"Sinal do Wi-Fi fraco ({signal:0}%)",
                "Com sinal fraco, a conexão fica lenta e cai com frequência, mesmo que o plano seja bom.",
                "Aproxime-se do roteador ou coloque-o em um local central e alto, longe de paredes grossas. " +
                "Em casas grandes, um repetidor ou sistema mesh ajuda.",
                Confidence.Alta));
        }
        else if (signal is < 60)
        {
            list.Add(new Finding("wifi.fair", Severity.Info, AreaWifi,
                $"Sinal do Wi-Fi razoável ({signal:0}%)",
                "O sinal funciona, mas pode limitar a velocidade em planos rápidos.",
                "Se a velocidade estiver abaixo do esperado, teste mais perto do roteador.",
                Confidence.Media));
        }

        var is24 = w.Has("band_24");
        if (is24 && w.Has("congested"))
        {
            list.Add(new Finding("wifi.congested", Severity.Warning, AreaWifi,
                "Canal do Wi-Fi congestionado",
                $"Há {w.Get("neighbors"):0} redes vizinhas disputando o mesmo canal ou canais sobrepostos " +
                "na faixa de 2,4 GHz. Elas competem pelo mesmo espaço e deixam todas mais lentas.",
                "Conecte-se à rede 5 GHz do roteador, se houver. Ou, nas configurações do roteador, " +
                "troque o canal para 1, 6 ou 11 (o menos usado).",
                Confidence.Media));
        }
        else if (w.Has("congested"))
        {
            list.Add(new Finding("wifi.congested5", Severity.Info, AreaWifi,
                "Outras redes no mesmo canal",
                $"Há {w.Get("neighbors"):0} redes vizinhas no mesmo canal.",
                "Se notar lentidão, troque o canal nas configurações do roteador.",
                Confidence.Baixa));
        }
        else if (is24 && signal >= 60)
        {
            list.Add(new Finding("wifi.band24", Severity.Info, AreaWifi,
                "Conectado na faixa de 2,4 GHz",
                "A faixa de 2,4 GHz alcança mais longe, mas é mais lenta e sofre mais interferência.",
                "Se o roteador tiver rede 5 GHz (geralmente com \"5G\" no nome), use-a quando estiver perto dele.",
                Confidence.Baixa));
        }
    }

    // ───────────── Limite do equipamento ─────────────

    /// <summary>Fração da taxa de conexão Wi-Fi que vira velocidade real (cabeçalhos, retransmissões, meio compartilhado).</summary>
    private const double WifiEfficiency = 0.6;

    /// <summary>Fração da velocidade do link Ethernet que vira velocidade real (cabeçalhos TCP/IP).</summary>
    private const double EthernetEfficiency = 0.94;

    /// <summary>O máximo que este computador consegue usar, e o que impõe esse máximo.</summary>
    private sealed record EquipmentCeiling(
        double Mbps,
        string Limiter,
        string Area,
        string Explanation,
        string Recommendation,
        Confidence Confidence,
        bool Misconfigured = false,
        string? FixId = null);

    /// <summary>
    /// Compara o plano (ou a velocidade medida) com o teto do equipamento. Evita o erro mais comum:
    /// culpar o provedor quando quem limita é a placa, o cabo ou o Wi-Fi do próprio computador.
    /// </summary>
    private static void EquipmentLimit(RuleContext c, List<Finding> list)
    {
        var ceiling = ComputeCeiling(c);
        if (ceiling is null) return;

        var plan = c.Options.ContractedDownloadMbps;
        var down = c.Speed?.Get("down_mbps");
        var approx = NetUtil.RoundEstimate(ceiling.Mbps);
        var measured = down is double d ? $" O teste mediu {d:0} Mbps." : "";

        if (plan is double p && ceiling.Mbps < p * 0.9)
        {
            list.Add(new Finding("pc.ceiling", Severity.Warning, ceiling.Area,
                $"Seu plano é de {p:0} Mbps, mas este computador só consegue usar até cerca de {approx:0} Mbps: {ceiling.Limiter}",
                ceiling.Explanation + measured,
                ceiling.Recommendation,
                ceiling.Confidence) { FixId = ceiling.FixId });
            return;
        }

        // Sem plano informado: avisa se a medição já bateu no teto, ou se a placa está mal configurada.
        var saturated = down is double dd && dd >= ceiling.Mbps * 0.8;
        if (plan is null && (saturated || ceiling.Misconfigured))
        {
            list.Add(new Finding("pc.ceiling", ceiling.Misconfigured ? Severity.Warning : Severity.Info, ceiling.Area,
                $"Este computador só consegue usar até cerca de {approx:0} Mbps: {ceiling.Limiter}",
                ceiling.Explanation + measured +
                (saturated ? " A velocidade medida já está nesse limite: um plano maior não deixaria este computador mais rápido." : ""),
                ceiling.Recommendation,
                ceiling.Confidence) { FixId = ceiling.FixId });
            return;
        }

        // Plano cabe no teto, mas a velocidade foi fixada manualmente: vale corrigir mesmo assim.
        if (ceiling.Misconfigured)
        {
            list.Add(new Finding("pc.speed_forced", Severity.Info, AreaPc,
                "A velocidade da placa de rede foi fixada manualmente",
                ceiling.Explanation, ceiling.Recommendation, Confidence.Alta) { FixId = ceiling.FixId });
        }
    }

    private static EquipmentCeiling? ComputeCeiling(RuleContext c)
    {
        var a = c.Adapter;
        if (a is null || a.Has("no_interface")) return null;
        return c.IsWireless ? WifiCeiling(c) : WiredCeiling(c);
    }

    private static EquipmentCeiling? WiredCeiling(RuleContext c)
    {
        if (c.Adapter!.Get("link_mbps") is not double link) return null;

        var cap = c.Capability;
        var max = cap?.Get("card_max_mbps");
        var forced = cap?.Has("speed_forced") == true;
        var effective = link * EthernetEfficiency;
        var usb = cap?.Has("usb_adapter") == true ? " USB" : "";

        if (forced && max is double m1 && link < m1)
        {
            return new EquipmentCeiling(effective,
                "a velocidade da placa de rede foi fixada manualmente", AreaPc,
                $"A placa suporta {NetUtil.FormatLink(m1)}, mas está configurada como " +
                $"\"{cap!.GetText("speed_setting")}\" em vez de negociar a velocidade automaticamente.",
                "No Gerenciador de Dispositivos, abra a placa de rede > aba Avançado > \"Speed & Duplex\" " +
                "(ou \"Velocidade e Duplex\") e escolha \"Auto Negotiation\".",
                Confidence.Alta, Misconfigured: true, FixId: "reset_speed_duplex");
        }

        if (max is double m2 && link < m2)
        {
            return new EquipmentCeiling(effective,
                "o cabo ou a porta do roteador é o limite", AreaPc,
                $"A placa suporta {NetUtil.FormatLink(m2)}, mas o link com o roteador foi negociado em apenas " +
                $"{NetUtil.FormatLink(link)}. Isso acontece com cabo antigo ou danificado (com só 2 dos 4 pares " +
                "funcionando) ou com porta de 100 Mbps no roteador.",
                "Troque o cabo por um Cat5e ou Cat6 e ligue-o em uma porta gigabit do roteador.",
                Confidence.Alta);
        }

        if (max is double m3)
        {
            return new EquipmentCeiling(effective,
                $"a placa de rede{usb} é o limite", AreaPc,
                $"A placa de rede{usb} deste computador suporta no máximo {NetUtil.FormatLink(m3)}.",
                "Uma placa de rede gigabit (USB 3.0 ou PCIe) é barata e resolve. Se o computador tiver " +
                "Wi-Fi 5 ou 6, pode valer comparar: perto do roteador ele pode ser mais rápido que esta placa.",
                Confidence.Alta);
        }

        return new EquipmentCeiling(effective,
            "a conexão de rede é o limite", AreaPc,
            $"A placa de rede negociou {NetUtil.FormatLink(link)} com o roteador. O driver não informa se esse " +
            "é o máximo da placa ou se o cabo/porta reduziu a velocidade.",
            "Teste com outro cabo (Cat5e ou Cat6) e outra porta do roteador. Se continuar igual, a placa é o limite.",
            Confidence.Media);
    }

    private static EquipmentCeiling? WifiCeiling(RuleContext c)
    {
        var w = c.Wifi;
        var rate = w?.Get("rx_mbps") ?? c.Adapter!.Get("link_mbps");
        if (rate is not double r) return null;

        var radio = w?.GetText("radio")?.Trim() ?? "";
        var band = w?.GetText("band");
        var signal = w?.Get("signal");

        // Wi-Fi 4 (802.11n) ou anterior: o próprio adaptador limita, por melhor que seja o sinal.
        var oldStandard = System.Text.RegularExpressions.Regex.IsMatch(radio, @"802\.11[abgn]$");

        var context = radio.Length > 0 ? $" ({radio}{(band is null ? "" : $", {band}")})" : "";
        var explanation = $"O Wi-Fi está conectado ao roteador a {NetUtil.FormatLink(r)}{context}. " +
                          "Na prática, o Wi-Fi entrega cerca de 60% da taxa de conexão.";

        var tips = new List<string>();
        if (oldStandard)
            tips.Add("Este adaptador usa um padrão antigo (Wi-Fi 4 ou anterior); um adaptador Wi-Fi 5 ou 6, " +
                     "USB ou interno, aumenta bastante esse limite.");
        if (signal is < 60)
            tips.Add($"O sinal está em {signal:0}%: ficar mais perto do roteador aumenta a taxa de conexão.");
        if (band is not null && band.StartsWith("2", StringComparison.Ordinal))
            tips.Add("Conectar na rede 5 GHz do roteador costuma multiplicar a taxa.");
        tips.Add("Para aproveitar o plano inteiro, o caminho mais garantido é o cabo de rede.");

        return new EquipmentCeiling(r * WifiEfficiency,
            oldStandard ? "o adaptador Wi-Fi é o limite" : "o Wi-Fi é o limite", AreaWifi,
            explanation, string.Join(" ", tips),
            oldStandard ? Confidence.Alta : Confidence.Media);
    }

    private static void Internet(RuleContext c, List<Finding> list)
    {
        var i = c.Internet;
        var g = c.Gateway;
        if (i is null) return;

        var gatewayOk = g is not null && RuleContext.Ran(g) && !g.Has("gw_unreachable") && !g.Has("gw_unstable");

        if (i.Has("offline"))
        {
            if (gatewayOk)
            {
                list.Add(new Finding("isp.offline", Severity.Critical, AreaIsp,
                    "O roteador funciona, mas não há acesso à internet",
                    "A comunicação com o roteador está normal, mas nada passa dele para fora. " +
                    "O problema está no modem, na fibra/cabo da rua ou no provedor.",
                    "Olhe as luzes do modem/ONU (luz vermelha ou \"LOS\" piscando indica problema na fibra). " +
                    "Reinicie modem e roteador. Se não voltar, ligue para o provedor.",
                    Confidence.Alta));
            }
            else
            {
                list.Add(new Finding("net.offline", Severity.Critical, AreaLan,
                    "Sem acesso à internet",
                    "Nenhum servidor externo respondeu e a rede local também apresenta problemas.",
                    "Resolva primeiro os problemas da rede local listados acima e rode o diagnóstico de novo.",
                    Confidence.Media));
            }
            return;
        }

        if (i.Has("captive_portal"))
        {
            list.Add(new Finding("net.captive_portal", Severity.Warning, AreaLan,
                "Esta rede exige login antes de liberar a internet",
                "A rede está redirecionando o acesso para uma página de autenticação (comum em hotéis, " +
                "aeroportos e redes de visitantes).",
                "Abra o navegador e acesse qualquer site para ver a página de login.",
                Confidence.Alta));
        }

        if (i.Has("icmp_blocked"))
        {
            list.Add(new Finding("net.icmp_blocked", Severity.Info, AreaLan,
                "A rede bloqueia ping",
                "A internet funciona, mas a rede não permite ping. Por isso, a rota até a internet não foi analisada.",
                "", Confidence.Alta));
        }

        if (!i.Has("inet_unstable")) return;

        var loss = i.Get("inet_loss") ?? 0;
        var avg = i.Get("inet_avg") ?? 0;
        var jitter = i.Get("inet_jitter") ?? 0;
        var traceConfirms = c.Trace?.Has("trace_loss") == true && c.Trace.Get("trace_loss_hop") > 1;

        if (g?.Has("gw_unstable") == true)
        {
            // A instabilidade já começa na rede local; o achado local explica isso.
            return;
        }

        if (loss > 2)
        {
            list.Add(new Finding("isp.loss", Severity.Warning, AreaIsp,
                $"Perda de pacotes na internet ({loss:0}%)",
                "A rede da sua casa está estável, mas parte dos dados se perde depois do roteador. " +
                "Isso causa lentidão, travamentos em vídeo e quedas em chamadas e jogos.",
                "Reinicie o modem. Se continuar, entre em contato com o provedor e envie este relatório.",
                traceConfirms ? Confidence.Alta : Confidence.Media));
        }

        if (avg > InternetCheck.HighLatencyMs)
        {
            list.Add(new Finding("isp.latency", Severity.Warning, AreaIsp,
                $"Latência alta para a internet ({avg:0} ms)",
                "O tempo de resposta até a internet está alto, embora a rede local esteja normal. " +
                "Sites demoram a começar a carregar e jogos ficam com atraso.",
                "Reinicie o modem. Conexões via rádio ou satélite têm latência naturalmente maior; " +
                "em fibra ou cabo, isso deve ser reportado ao provedor.",
                Confidence.Media));
        }

        if (jitter > InternetCheck.HighJitterMs)
        {
            list.Add(new Finding("isp.jitter", Severity.Warning, AreaIsp,
                $"Conexão oscilando (variação de {jitter:0} ms)",
                "O tempo de resposta varia muito de um instante para outro. Isso prejudica chamadas de vídeo, " +
                "jogos online e transmissões ao vivo.",
                "Verifique se alguém está usando muita banda ao mesmo tempo. Se persistir, contate o provedor.",
                Confidence.Media));
        }
    }

    private static void Dns(RuleContext c, List<Finding> list)
    {
        var d = c.Dns;
        if (d is null) return;

        var bestPublic = d.GetText("dns_pub_server") ?? "1.1.1.1";

        if (d.Has("dns_fail"))
        {
            list.Add(new Finding("dns.fail", Severity.Critical, AreaDns,
                "Os nomes de sites não estão sendo encontrados (falha de DNS)",
                "A internet responde, mas o computador não consegue converter nomes de sites em endereços. " +
                "Sintoma típico: \"site não encontrado\" com a internet aparentemente conectada.",
                $"Troque o DNS do adaptador para {bestPublic} e 8.8.8.8, e reinicie o roteador.",
                Confidence.Alta) { FixId = "set_public_dns" });
            return;
        }

        if (d.Has("dns_system_fail"))
        {
            list.Add(new Finding("dns.system_fail", Severity.Warning, AreaDns,
                "O servidor DNS configurado não está respondendo",
                "As consultas ao DNS configurado falharam, enquanto DNS públicos responderam normalmente. " +
                "Sites podem demorar ou falhar ao abrir.",
                $"Troque o DNS do adaptador (ou do roteador) para {bestPublic} e 8.8.8.8.",
                Confidence.Alta) { FixId = "set_public_dns" });
        }

        if (d.Has("dns_slow"))
        {
            list.Add(new Finding("dns.slow", Severity.Warning, AreaDns,
                "DNS lento",
                $"Seu DNS responde em {d.Get("dns_sys_ms"):0} ms; o DNS público {bestPublic} responde em " +
                $"{d.Get("dns_pub_ms"):0} ms. Cada site novo demora mais para começar a abrir, " +
                "mesmo com boa velocidade.",
                $"Troque o DNS para {bestPublic} e 8.8.8.8 no adaptador de rede ou no roteador.",
                Confidence.Alta) { FixId = "set_public_dns" });
        }

        if (d.Has("no_dns"))
        {
            list.Add(new Finding("dns.none", Severity.Warning, AreaDns,
                "Nenhum servidor DNS configurado",
                "O adaptador não recebeu endereço de DNS do roteador.",
                $"Configure o DNS manualmente para {bestPublic} e 8.8.8.8, ou reinicie o roteador.",
                Confidence.Media) { FixId = "set_public_dns" });
        }
    }

    private static void Route(RuleContext c, List<Finding> list)
    {
        var t = c.Trace;
        if (t is null || !RuleContext.Ran(t)) return;

        if (t.Has("trace_loss"))
        {
            var hop = t.Get("trace_loss_hop") ?? 0;
            var ip = t.GetText("trace_loss_ip");

            if (hop <= 1)
            {
                list.Add(new Finding("route.loss_router", Severity.Warning, AreaLan,
                    "A perda de pacotes começa no seu roteador",
                    $"O primeiro equipamento do caminho ({ip}) já descarta parte dos dados.",
                    "Reinicie o roteador. Se o problema voltar com frequência, ele pode estar sobrecarregado " +
                    "ou precisar ser substituído.",
                    Confidence.Media));
            }
            else
            {
                var detail = t.Has("trace_loss_private") && hop == 2
                    ? "Esse salto tem endereço privado: pode ser o modem do provedor ou um segundo roteador na sua rede."
                    : "Esse ponto já está fora da sua casa, na rede do provedor.";

                list.Add(new Finding("route.loss_isp", Severity.Warning, AreaIsp,
                    $"Perda de pacotes a partir do salto {hop:0} ({ip})",
                    $"A rede local está passando os dados normalmente, mas a perda começa no salto {hop:0} " +
                    $"e continua até o destino. {detail}",
                    "Contate o provedor e envie este relatório: ele mostra exatamente onde a perda começa.",
                    Confidence.Alta));
            }
        }
        else if (t.Has("trace_jump"))
        {
            var ms = t.Get("trace_jump_ms") ?? 0;
            list.Add(new Finding("route.jump", ms >= 100 ? Severity.Warning : Severity.Info, AreaIsp,
                $"Atraso de {ms:0} ms surge no salto {t.Get("trace_jump_hop"):0}",
                $"A partir de {t.GetText("trace_jump_ip")}, o tempo de resposta sobe e permanece alto até o destino. " +
                "Pode indicar rota longa ou congestionamento na rede do provedor.",
                "Se a lentidão for frequente, informe esse ponto ao provedor.",
                Confidence.Baixa));
        }
        else if (t.Has("trace_stops"))
        {
            list.Add(new Finding("route.stops", Severity.Info, AreaIsp,
                "A rota até o servidor de teste não foi concluída",
                "Algum equipamento no caminho não repassou os pacotes de teste. Isso pode ser apenas uma " +
                "configuração de bloqueio, sem afetar a navegação.",
                "", Confidence.Baixa));
        }
    }

    private static void Speed(RuleContext c, List<Finding> list)
    {
        var s = c.Speed;
        if (s is null || !RuleContext.Ran(s)) return;

        if (s.Has("speed_failed"))
        {
            list.Add(new Finding("speed.failed", Severity.Info, AreaSpeed,
                "Não foi possível medir a velocidade",
                "O servidor de teste não respondeu. Firewalls de empresa às vezes bloqueiam esse tipo de teste.",
                "", Confidence.Baixa));
            return;
        }

        var down = s.Get("down_mbps");
        var up = s.Get("up_mbps");
        var contracted = c.Options.ContractedDownloadMbps;
        var wifiHint = c.IsWireless
            ? " A medição foi feita pelo Wi-Fi; antes de reclamar com o provedor, repita o teste com cabo."
            : "";

        // Se a medição está no teto do equipamento, o achado "pc.ceiling" já explica a lentidão;
        // não mandar o usuário reclamar com o provedor por algo que está no computador dele.
        var ceiling = ComputeCeiling(c);
        bool EquipmentExplains(double measured) => ceiling is not null && measured >= ceiling.Mbps * 0.7;

        if (contracted is double plan && down is double d)
        {
            var pct = d / plan * 100;
            if (pct < 50 && !EquipmentExplains(d))
            {
                list.Add(new Finding("speed.below_plan", Severity.Warning, AreaSpeed,
                    $"Velocidade abaixo do contratado ({d:0} de {plan:0} Mbps)",
                    $"O download medido foi {pct:0}% do plano." + wifiHint,
                    c.IsWireless
                        ? "Teste com cabo. Se continuar abaixo, contate o provedor e envie este relatório."
                        : "Reinicie o modem. Se continuar, contate o provedor e envie este relatório.",
                    c.IsWireless ? Confidence.Media : Confidence.Alta));
            }
        }
        else if (down is double low && low < 10 && !EquipmentExplains(low))
        {
            list.Add(new Finding("speed.low_down", Severity.Warning, AreaSpeed,
                $"Velocidade de download baixa ({down:0.#} Mbps)",
                "Abaixo de 10 Mbps, vídeos em alta resolução e downloads ficam lentos." + wifiHint,
                "Informe a velocidade contratada para uma comparação precisa. Se for bem maior que isso, " +
                "contate o provedor.",
                Confidence.Media));
        }

        if (up < 2)
        {
            list.Add(new Finding("speed.low_up", Severity.Warning, AreaSpeed,
                $"Velocidade de upload baixa ({up:0.#} Mbps)",
                "Upload baixo prejudica chamadas de vídeo, envio de arquivos e backups na nuvem.",
                "Verifique se algum programa está enviando arquivos (backup, sincronização de nuvem). " +
                "Se não, confira o upload do seu plano com o provedor.",
                Confidence.Media));
        }

        var bloat = s.Get("bloat_ms");
        if (bloat >= 100)
        {
            list.Add(new Finding("speed.bufferbloat", Severity.Warning, AreaLan,
                "A conexão trava quando está em uso (bufferbloat)",
                $"Em repouso a latência é {s.Get("idle_ms"):0} ms, mas sobe {bloat:0} ms quando a internet está " +
                "sendo usada. Por isso chamadas e jogos travam quando alguém assiste vídeo ou baixa algo, " +
                "mesmo com boa velocidade.",
                "Ative QoS ou SQM nas configurações do roteador, se houver. Roteadores antigos ou do próprio " +
                "provedor costumam ter esse problema; trocar o roteador geralmente resolve.",
                Confidence.Alta));
        }
        else if (bloat >= 50)
        {
            list.Add(new Finding("speed.bufferbloat_mild", Severity.Info, AreaLan,
                "A latência aumenta um pouco quando a conexão está em uso",
                $"A latência sobe {bloat:0} ms durante downloads. Pode causar pequenos atrasos em jogos.",
                "Se jogar online, ative QoS no roteador.",
                Confidence.Media));
        }
    }

    private static void Environment(RuleContext c, List<Finding> list)
    {
        var a = c.Adapter;
        if (a is null) return;

        if (a.Has("vpn"))
        {
            list.Add(new Finding("pc.vpn", Severity.Info, AreaPc,
                "VPN ativa",
                $"Há uma VPN conectada ({a.GetText("vpn")}). VPNs costumam reduzir a velocidade e aumentar a latência.",
                "Para comparar, desconecte a VPN e rode o diagnóstico de novo.",
                Confidence.Media));
        }

        if (a.Has("proxy"))
        {
            list.Add(new Finding("pc.proxy", Severity.Info, AreaPc,
                "Proxy configurado",
                $"O Windows está usando um proxy ({a.GetText("proxy")}). Proxies mal configurados deixam a navegação lenta.",
                "Se não for uma exigência da sua empresa, desative em Configurações > Rede e Internet > Proxy.",
                Confidence.Baixa));
        }
    }
}
