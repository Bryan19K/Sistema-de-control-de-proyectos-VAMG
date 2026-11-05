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

    /// <summary>Nombre con el que se guarda la plantilla completa en el sistema.</summary>
    public const string FtthCompleteName = "Plan FTTH completo (feeder, ODN y fusiones)";

    /// <summary>Días que dura cada tarea cuando el cronograma de origen no trae duraciones.</summary>
    public const int DefaultTaskDays = 2;

    /// <summary>
    /// Plan FTTH completo armado con el cronograma de MS Project (doce fases: autorización, carga laboral, feeder, ODN,
    /// certificación y cierre) más una fase de fusiones de la ODN y una actividad por nodo. El cronograma no trae duraciones:
    /// cada tarea dura <see cref="DefaultTaskDays"/> días y se ajusta al aplicar la plantilla.
    /// </summary>
    public static PlanTemplateDefinition FtthComplete()
    {
        (string Phase, string[] Tasks)[] wbs =
        [
            ("1. Autorización de diseño", ["1.1 Revisión y aprobación del diseño de red"]),
            ("2. Distribución de carga laboral", ["2.1 Asignación de equipos para feeder", "2.2 Asignación de equipos para nodos (ODN)"]),
            ("3. Feeder – Diseño", ["3.1 Definición y validación del diseño del feeder", "3.2 Asignación y distribución de hilos", "3.3 Diagrama de fusiones en el ODF"]),
            ("4. Feeder – Levantamiento y permisos", ["4.1 Levantamiento de campo de la ruta del feeder", "4.2 Gestión de permisos de postes y servidumbres"]),
            ("5. Feeder – Materiales", ["5.1 Revisión de materiales requeridos para el ODF", "5.2 Revisión de materiales para todo el feeder", "5.3 Confirmación de materiales en sitio"]),
            ("6. Feeder – Instalación en el nodo de telecomunicaciones",
                ["6.1 Instalación del ODF", "6.2 Instalación de acopladores", "6.3 Instalación de pigtails", "6.4 Preparación de cable", "6.5 Preparación de hilos", "6.6 Fusiones", "6.7 Pruebas ópticas"]),
            ("7. Feeder – Tendido de cable",
                ["7.1 Tendido de cable de 96 hilos", "7.2 Tendido de cable de 48 hilos", "7.3 Tendido de cable de 24 hilos", "7.4 Tendido de cable de 12 hilos", "7.5 Instalación de mufas o cierres ópticos con fusiones"]),
            ("8. Feeder – Cierre", ["8.1 Pruebas de continuidad del ODF al último extremo del feeder", "8.2 Documentación as-built del feeder"]),
            ("9. ODN – Identificación en campo", ["9.1 Identificación del diseño en campo", "9.2 Visto bueno del diseño en campo"]),
            ("10. ODN – Montaje de infraestructura por nodo",
                ["10.1 Montaje del X-box", "10.2 Tendido de cable MPO del X-box al hub box", "10.3 Montaje del hub box", "10.4 Identificación y colocación de NAP de paso",
                 "10.5 Identificación y colocación de NAP final", "10.6 Tendido de cables de interconexión de un hilo hasta la NAP final"]),
            ("10b. ODN – Fusiones", ["Fusiones en X-box", "Fusiones en hub box", "Fusiones en NAP de paso", "Fusiones en NAP final", "Cruces americanos"]),
            ("11. Certificación y pruebas",
                ["11.1 Medición en cada puerto de NAP de paso y NAP final", "11.2 Verificación del presupuesto óptico dentro de niveles acordados",
                 "11.3 Verificación de concordancia con la georreferenciación", "11.4 Informe de certificación con mediciones"]),
            ("12. Cierre del proyecto", ["12.1 Documentación as-built de la ODN con georreferenciación", "12.2 Entrega y aceptación final de la red"]),
        ];

        var day = 0;
        var phases = new List<PlanTemplatePhase>();
        var odnStart = 0;
        foreach (var (phase, tasks) in wbs)
        {
            if (phase.StartsWith("9.", StringComparison.Ordinal))
            {
                odnStart = day;
            }

            var activities = new List<PlanTemplateActivity>();
            foreach (var task in tasks)
            {
                activities.Add(new PlanTemplateActivity(task, day, DefaultTaskDays));
                day += DefaultTaskDays;
            }

            phases.Add(new PlanTemplatePhase(phase, activities));
        }

        // Una actividad por nodo para ver cada nodo en el Gantt: construcción de ODN, fusiones y certificación.
        phases.Insert(phases.FindIndex(p => p.Name.StartsWith("11.", StringComparison.Ordinal)),
            new PlanTemplatePhase("ODN por nodo", [new PlanTemplateActivity("Nodo {nodo} · ODN y fusiones", odnStart, 14, PerSite: true)]));
        phases.Add(new PlanTemplatePhase("Hito final", [new PlanTemplateActivity("Entrega al cliente", day, 0, Milestone: true)]));

        return new PlanTemplateDefinition(Math.Max(8, (int)Math.Ceiling(day / 7.0)), phases);
    }

    /// <summary>Plan general de 4 semanas para proyectos sin parte técnica.</summary>
    public static PlanTemplateDefinition StandardGeneral() => new(4,
    [
        new("Planificación", [new("Definir alcance y responsables", 0, 7)]),
        new("Ejecución", [new("Ejecutar actividades principales", 7, 14)]),
        new("Cierre", [new("Revisión y entrega", 21, 7), new("Cierre del proyecto", 27, 0, Milestone: true)]),
    ]);
}
