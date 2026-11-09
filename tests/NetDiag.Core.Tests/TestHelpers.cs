using System.Globalization;

namespace NetDiag.Core.Tests;

/// <summary>Troca a cultura da thread durante um teste, para que a formatação numérica seja previsível.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous;

    public CultureScope(string name)
    {
        _previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
    }

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
