using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Web.Components.Shared;

public static class FeederLabels
{
    public static string Name(FeederItemKind kind) => kind switch
    {
        FeederItemKind.Cable12 => "Cable feeder 12 hilos",
        FeederItemKind.Cable24 => "Cable feeder 24 hilos",
        FeederItemKind.Cable48 => "Cable feeder 48 hilos",
        FeederItemKind.Cable96 => "Cable feeder 96 hilos",
        FeederItemKind.Closure12 => "Cierre 12H",
        FeederItemKind.Closure24 => "Cierre 24H",
        FeederItemKind.Closure48 => "Cierre 48H",
        FeederItemKind.Closure96 => "Cierre 96H",
        FeederItemKind.Odf96 => "ODF 96H",
        _ => kind.ToString(),
    };

    public static string Unit(FeederItemKind kind) => kind <= FeederItemKind.Cable96 ? "m" : "u";
}
