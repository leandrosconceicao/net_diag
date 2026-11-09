using System.Reflection;
using NetDiag.Core.Net;

namespace NetDiag.Core.Tests;

/// <summary>Testa o pacote DNS montado por DnsProbe (método privado, acessado por reflexão).</summary>
public class DnsProbeTests
{
    private static byte[] BuildQuery(string name, ushort id)
    {
        var method = typeof(DnsProbe).GetMethod("BuildQuery", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (byte[])method.Invoke(null, new object[] { name, id })!;
    }

    [Fact]
    public void Header_carries_id_recursion_flag_and_one_question()
    {
        var q = BuildQuery("example.com", 0xABCD);

        Assert.Equal(0xAB, q[0]);
        Assert.Equal(0xCD, q[1]);
        Assert.Equal(0x01, q[2]); // recursão desejada
        Assert.Equal(0x00, q[3]);
        Assert.Equal(0x00, q[4]); // QDCOUNT = 1
        Assert.Equal(0x01, q[5]);
    }

    [Fact]
    public void Question_encodes_labels_then_type_A_and_class_IN()
    {
        var q = BuildQuery("www.example.com", 1);

        var expectedQuestion = new byte[]
        {
            3, 0x77, 0x77, 0x77,                          // www
            7, 0x65, 0x78, 0x61, 0x6D, 0x70, 0x6C, 0x65,  // example
            3, 0x63, 0x6F, 0x6D,                          // com
            0,                                            // fim do nome
            0x00, 0x01,                                   // QTYPE = A
            0x00, 0x01                                    // QCLASS = IN
        };
        Assert.Equal(expectedQuestion, q[12..]);
    }

    [Fact]
    public void Trailing_dot_does_not_change_the_packet()
    {
        Assert.Equal(BuildQuery("example.com", 7), BuildQuery("example.com.", 7));
    }
}
