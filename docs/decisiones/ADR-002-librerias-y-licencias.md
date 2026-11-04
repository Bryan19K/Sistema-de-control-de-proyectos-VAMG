# ADR-002 · Librerías de terceros y sus licencias

- **Estado:** aceptada
- **Fecha:** 2026-11-03
- **Sprint:** 0

## Contexto

TEK-COM es una empresa, así que las librerías deben poder usarse con fines comerciales sin
costo. Varias librerías populares cambiaron de licencia recientemente.

## Decisión

| Librería | Versión | Licencia | Nota |
|---|---|---|---|
| MudBlazor | 9.x | MIT | Sin restricciones. |
| EF Core, ASP.NET Core Identity | 10.x | MIT | Sin restricciones. |
| xUnit | 2.9.x | Apache 2.0 | Sin restricciones. |
| **FluentAssertions** | **7.2.x (fijada)** | Apache 2.0 | **La versión 8+ pasó a licencia comercial de pago.** No actualizar a 8.x. Si hace falta una versión más nueva, migrar a *AwesomeAssertions* (fork con licencia Apache 2.0 y la misma API). |
| ClosedXML | 0.105.x | MIT | Sin restricciones. |
| QuestPDF | 2026.9.x | Community | Gratuita solo para empresas con ingresos anuales menores a 1 millón USD. **TEK-COM debe confirmar que cumple ese límite**; si no, hay que comprar la licencia correspondiente. El código ya declara `QuestPDF.Settings.License = LicenseType.Community` (en `ReportExporter`). |
| Hangfire (Core, AspNetCore) | 1.8.x | LGPL v3 | La versión Core es gratuita para uso comercial. No se usa Hangfire Pro. |
| Hangfire.InMemory | 1.0.x | LGPL v3 | Solo para desarrollo y pruebas con SQLite; en PostgreSQL los trabajos se guardan en la base. |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.x | PostgreSQL License | Sin restricciones. |
| Hangfire.PostgreSql | 1.21.x | Apache 2.0 | Almacenamiento de Hangfire en PostgreSQL. |
| MailKit | 4.18.x | MIT | Sin restricciones. Envío de correo por SMTP. |

Las versiones se administran de forma centralizada en `Directory.Packages.props`
(Central Package Management) con fijado transitivo, y `NuGetAuditMode=all` hace fallar la
compilación si alguna dependencia tiene una vulnerabilidad conocida.

## Consecuencias

- Toda librería nueva se agrega a esta tabla con su licencia antes de usarla.
- Las actualizaciones de versión se hacen en un solo archivo.
