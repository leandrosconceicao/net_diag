# NetDiag — Diagnóstico de Conexão para Windows

Programa com interface gráfica (WPF / .NET 10) que diagnostica problemas de conexão (internet lenta, quedas, instabilidade) e aponta **onde** está o problema: no computador, no Wi-Fi, no roteador, no DNS ou no provedor.

## Requisitos

- Windows 10 ou 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) para compilar (quem só usa o `.exe` publicado não precisa de nada)
- Não exige administrador

## Rodar e publicar

```powershell
# rodar em modo de desenvolvimento
dotnet run --project src/NetDiag.App

# gerar um único NetDiag.exe que roda em qualquer Windows x64, sem instalar o .NET
dotnet publish src/NetDiag.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
```

O executável fica em `publish/NetDiag.exe`.

## Como o diagnóstico funciona

Os testes rodam **de dentro para fora**. Comparar uma camada com a anterior é o que permite separar problema local de problema do provedor.

| # | Teste (`Checks/`) | O que mede | Exemplos de causa detectada |
|---|---|---|---|
| 1 | `AdapterCheck` | adaptador ativo, IP, gateway, DNS, link, erros, VPN, proxy | cabo solto, DHCP falhou (169.254.x.x), link a 100 Mbps |
| 2 | `WifiCheck` | sinal, faixa, canal, redes vizinhas (`netsh wlan`) | sinal fraco, canal congestionado em 2,4 GHz |
| 2b | `CapabilityCheck` | máximo suportado pela placa, velocidade fixada manualmente, duplex, adaptador USB (WMI) | placa Fast Ethernet, cabo negociando abaixo da placa, half-duplex |
| 3 | `GatewayCheck` | 30 pings ao roteador: latência, jitter, perda | Wi-Fi/cabo instável, roteador travado |
| 4 | `InternetCheck` | ping + TCP a 1.1.1.1 e 8.8.8.8, portal cativo | sem internet com roteador OK, perda no provedor |
| 5 | `DnsCheck` | consulta UDP direta ao DNS configurado vs. públicos | DNS fora do ar, DNS lento |
| 6 | `TracerouteCheck` | saltos até 8.8.8.8 (em paralelo) | salto onde a perda/atraso começa |
| 7 | `SpeedCheck` | download/upload (Cloudflare), latência sob carga | velocidade abaixo do plano, bufferbloat |

### Teto do equipamento

Antes de culpar o provedor, o programa calcula o máximo que **este computador** consegue usar:

- **Cabo:** a velocidade negociada × 0,94. A causa é identificada comparando com o máximo da placa: velocidade fixada manualmente, cabo/porta abaixo da placa, ou a própria placa.
- **Wi-Fi:** a taxa de conexão × 0,6. Wi-Fi 4 (802.11n) ou anterior é apontado como limite do adaptador.

Se o teto for menor que o plano, a conclusão fica assim: *"Seu plano é de 500 Mbps, mas este computador só consegue usar até cerca de 90 Mbps: o adaptador Wi-Fi é o limite"*. Quando a velocidade medida está nesse teto, o programa não recomenda reclamar com o provedor.

Depois, `Rules/BuiltInRules.cs` cruza as medições e gera as conclusões (`Finding`): título, explicação em linguagem simples, recomendação, área e grau de confiança.

## Estrutura

```
src/
  NetDiag.Core/            biblioteca sem interface (reutilizável)
    Abstractions/          IDiagnosticCheck, IDiagnosticRule, IRemediation
    Checks/                um arquivo por camada
    Net/                   Probe (ping/TCP), DnsProbe, Shell (netsh), utilitários
    Rules/                 BuiltInRules
    Reporting/             HtmlReportWriter
    DiagnosticRunner.cs    executa os testes e aplica as regras
  NetDiag.App/             interface WPF (MVVM sem dependências externas)
```

## Como estender

**Novo teste:** implemente `IDiagnosticCheck`, grave métricas com `result.Set(...)` e fatos com `result.Flags.Add(...)`, e registre em `DiagnosticRunner.CreateDefault()`.

**Nova regra:** implemente `IDiagnosticRule` (ou adicione um método em `BuiltInRules`) lendo `context.Report.Get("id-do-teste")`.

**Monitoramento contínuo (futuro):** o `DiagnosticRunner` não depende da interface. Um modo de monitoramento pode chamá-lo periodicamente (ou só os testes leves 3–5), gravar os relatórios em SQLite e mostrar um histórico de quedas.

**Correções automáticas (futuro):** cada `Finding` já tem um `FixId` (`renew_ip`, `set_public_dns`…). Basta implementar `IRemediation` para cada um e exibir um botão "Corrigir" no cartão. Correções que exigem administrador devem abrir um processo elevado só para aquela ação.

## Limitações conhecidas

- **Windows 11 24H2+:** o `netsh wlan` exige permissão de localização. Sem ela, o teste de Wi-Fi avisa e explica como liberar.
- **Rede de empresa:** se ping (ICMP) for bloqueado, a latência é medida por conexão TCP e o traceroute é pulado. Se o servidor de teste de velocidade for bloqueado, o teste avisa.
- **Medição de velocidade:** downloads e vídeos rodando ao mesmo tempo reduzem o resultado. O consumo de dados é de algumas centenas de MB em conexões rápidas.
- **Textos do `netsh`:** a leitura reconhece Windows em português e inglês. Outros idiomas mostram menos detalhes de Wi-Fi.

## Próximos passos sugeridos

1. Testes unitários das regras (montar um `DiagnosticReport` falso e conferir os `Finding`s).
2. Consumo de banda por processo (descobrir quem está usando a rede).
3. Ícone, assinatura do executável e instalador.
4. Modo monitoramento e correções automáticas.
