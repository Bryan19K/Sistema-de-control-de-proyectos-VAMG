using System.Text.Json;

namespace Tekcom.Gestion.Domain.Logic;

public sealed record PlanTemplateActivity(string Name, int StartDay, int DurationDays, bool Milestone = false, bool PerSite = false);

public sealed record PlanTemplatePhase(string Name, List<PlanTemplateActivity> Activities);

/// <summary>Contenido de una plantilla de plan. Los días cuentan desde la fecha de inicio del proyecto.</summary>
public sealed record PlanTemplateDefinition(int Weeks, List<PlanTemplatePhase> Phases)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static PlanTemplateDefinition FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PlanTemplateDefinition>(json, Options) ?? new PlanTemplateDefinition(0, []);
        }
        catch (JsonException)
        {
            return new PlanTemplateDefinition(0, []);
        }
    }

    /// <summary>"Plan FTTH estándar": 8 semanas con preparación, construcción de ODN por nodo y cierre.</summary>
    public static PlanTemplateDefinition StandardFtth() => new(8,
    [
        new("Fase 1 · Preparación",
        [
            new("Recepción de materiales", 0, 7),
            new("Instalación de OLT", 7, 7),
        ]),
        new("Fase 2 · Construcción ODN",
        [
            new("Nodo {nodo}", 14, 21, PerSite: true),
        ]),
        new("Fase 3 · Cierre",
        [
            new("Pruebas y ATP", 42, 7),
            new("Entrega al cliente", 55, 0, Milestone: true),
        ]),
    ]);

    /// <summary>Plan general de 4 semanas para proyectos sin parte técnica.</summary>
    public static PlanTemplateDefinition StandardGeneral() => new(4,
    [
        new("Planificación", [new("Definir alcance y responsables", 0, 7)]),
        new("Ejecución", [new("Ejecutar actividades principales", 7, 14)]),
        new("Cierre", [new("Revisión y entrega", 21, 7), new("Cierre del proyecto", 27, 0, Milestone: true)]),
    ]);
}
