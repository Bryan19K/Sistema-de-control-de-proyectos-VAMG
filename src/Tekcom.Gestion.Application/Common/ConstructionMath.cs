using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Application.Common;

/// <summary>Un movimiento de instalado de una pieza de la ODN o de fusión.</summary>
public sealed record PieceMove(int SiteId, QuantityGroup Group, DateOnly Date, decimal Quantity);

/// <summary>Lo que hace falta de un nodo para convertir piezas instaladas en home passes.</summary>
public sealed record NodeHpBasis(int HomesPassed, IReadOnlyDictionary<QuantityGroup, decimal> ProjectedByGroup);

/// <summary>Cálculos de la construcción: permisos y home passes construidos por semana.</summary>
public static class ConstructionMath
{
    public static readonly QuantityGroup[] PieceGroups =
    [
        QuantityGroup.FcCable, QuantityGroup.MpoCable, QuantityGroup.SubBox, QuantityGroup.EndBox,
        QuantityGroup.HubBox, QuantityGroup.XBox, QuantityGroup.Fusion, QuantityGroup.Crossing,
    ];

    public static string PermitName(PermitStatus status) => status switch
    {
        PermitStatus.InProcess => "PERMISO EN TRÁMITE",
        PermitStatus.Approved => "PERMISO APROBADO",
        _ => "SOLICITUD PENDIENTE",
    };

    /// <summary>Lee el texto que traía el Excel ("APROBADO", "EN TRAMITE", "PENDIENTE"…).</summary>
    public static PermitStatus PermitFromText(string? text)
    {
        var t = (text ?? string.Empty).Trim().ToUpperInvariant();
        if (t.Contains("APROB", StringComparison.Ordinal)) { return PermitStatus.Approved; }
        if (t.Contains("TRAMIT", StringComparison.Ordinal) || t.Contains("TRÁMIT", StringComparison.Ordinal) || t.Contains("PROCESO", StringComparison.Ordinal)) { return PermitStatus.InProcess; }
        return PermitStatus.RequestPending;
    }

    /// <summary>
    /// Fechas del permiso al cambiar de estado: pasar a trámite marca la solicitud y aprobar marca la aprobación
    /// (con la solicitud si faltaba); volver a un estado anterior borra lo que ya no corresponde.
    /// </summary>
    public static (DateOnly? RequestedOn, DateOnly? ApprovedOn) PermitDates(PermitStatus status, DateOnly? requestedOn, DateOnly? approvedOn, DateOnly today) => status switch
    {
        PermitStatus.RequestPending => (null, null),
        PermitStatus.InProcess => (requestedOn ?? today, null),
        _ => (requestedOn ?? today, approvedOn ?? today),
    };

    /// <summary>Días del trámite: de la solicitud a la aprobación, o hasta hoy si sigue en trámite. Vacío si no se ha solicitado.</summary>
    public static int? PermitDays(DateOnly? requestedOn, DateOnly? approvedOn, DateOnly today) =>
        requestedOn is null ? null : Math.Max(0, (approvedOn ?? today).DayNumber - requestedOn.Value.DayNumber);

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Home passes equivalentes construidos por semana: cada pieza instalada (sin pasarse de lo proyectado de su grupo)
    /// aporta la parte que representa del total de piezas del nodo multiplicada por los HP del nodo.
    /// </summary>
    public static Dictionary<DateOnly, decimal> WeeklyHp(IEnumerable<PieceMove> moves, IReadOnlyDictionary<int, NodeHpBasis> nodes)
    {
        var weeks = new Dictionary<DateOnly, decimal>();
        foreach (var bySite in moves.Where(m => nodes.ContainsKey(m.SiteId)).GroupBy(m => m.SiteId))
        {
            var node = nodes[bySite.Key];
            var total = node.ProjectedByGroup.Where(p => PieceGroups.Contains(p.Key)).Sum(p => p.Value);
            if (node.HomesPassed <= 0 || total <= 0)
            {
                continue;
            }

            foreach (var byGroup in bySite.GroupBy(m => m.Group))
            {
                var projected = node.ProjectedByGroup.GetValueOrDefault(byGroup.Key);
                if (projected <= 0) { continue; }

                decimal cumulative = 0;
                foreach (var move in byGroup.OrderBy(m => m.Date))
                {
                    var next = Math.Clamp(cumulative + move.Quantity, 0m, projected);
                    var delta = next - cumulative;
                    cumulative = next;
                    if (delta == 0) { continue; }

                    var week = WeekStart(move.Date);
                    weeks[week] = weeks.GetValueOrDefault(week) + delta / total * node.HomesPassed;
                }
            }
        }

        return weeks.ToDictionary(w => w.Key, w => Math.Round(w.Value, 1));
    }
}
