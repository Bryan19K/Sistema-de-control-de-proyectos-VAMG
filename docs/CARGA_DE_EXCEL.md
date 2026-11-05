# Carga de Excel

Todo se sube desde **Importar Excel** (solo administrador). El sistema detecta qué trae el archivo, muestra un reporte antes de guardar y valida los totales contra el Excel. Subir el mismo archivo otra vez no duplica nada.

| Archivo | Qué carga |
|---|---|
| `PROYECTO_FTTH_...xlsx` | Nodos, avance, feeder y, si trae las hojas de bodega: ingresos por entrega del proveedor, entregas por nodo/brigada/feeder/O&M y SFP con serial |
| `TEK-COM - CONTROL DE PROYECTOS...xlsx` | Cinco proyectos de edificios (centro de costos = código): fases, apartamentos, infraestructura, niveles y materiales |
| `PROYECTOS NEXT-WAVE...xlsx` | Control de materiales por proyecto (reemplaza al resumen del control de proyectos) |
| `GASTOS PROYECTO FTTH...xlsx` | Compras con IVA, caja chica y viáticos con saldo por fondo (se enlazan al proyecto por centro de costos) |
| `PRECIARIO FINAL...xlsx` | Precios target, órdenes por nodo y códigos ERP del catálogo |
| `BASE DE DATOS CONTRATISTAS...xlsx` | Contratistas y su personal con póliza o afiliación (Optical Precision no se carga y se da de baja) |

Orden recomendado: primero el control FTTH, luego los demás. Los Excel con datos del negocio **no se suben al repositorio**.

No se cargan las hojas de personal (DUI y teléfonos), los balances por brigada (`E-C`, `H-B`, `O-P`) ni los BOM por nodo.

## El sistema es el maestro

Lo que se carga desde Excel se puede **editar, agregar y quitar** desde las pantallas (solo administrador). Para no pisar
lo editado, volver a subir un Excel solo agrega lo nuevo y completa lo vacío: no sobrescribe gastos, órdenes, precios,
avance de edificios, ni datos de contratistas que ya existan.

- Lo instalado de un nodo se corrige con el lápiz de la tabla "Cantidades por material"; el avance global del nodo se calcula por piezas instaladas.
- **Cronograma de nodos**: Gantt con todos los nodos y su trazabilidad.
- Plantilla del plan "Plan FTTH completo (feeder, ODN y fusiones)" armada con el cronograma de MS Project (el cronograma no trae duraciones: cada tarea dura 2 días por defecto).
