namespace Tekcom.Gestion.Web.Services;

/// <summary>Tema claro u oscuro del usuario en su sesión. El valor se recuerda en el navegador (localStorage).</summary>
public sealed class ThemeState
{
    public bool IsDark { get; private set; }

    public event Action? Changed;

    public void Set(bool dark)
    {
        if (IsDark == dark) { return; }
        IsDark = dark;
        Changed?.Invoke();
    }
}
