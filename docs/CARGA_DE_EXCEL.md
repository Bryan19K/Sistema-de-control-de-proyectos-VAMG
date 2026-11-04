# Carga de Excel

Todo se sube desde **Importar Excel** (solo administrador). El sistema detecta qué trae el archivo, muestra un reporte antes de guardar y valida los totales contra el Excel. Subir el mismo archivo otra vez no duplica nada.

| Archivo | Qué carga |
|---|---|
| `PROYECTO_FTTH_...xlsx` | Nodos, avance, feeder y, si trae las hojas de bodega: ingresos por entrega del proveedor, entregas por nodo/brigada/feeder/O&M y SFP con serial |
| `TEK-COM - CONTROL DE PROYECTOS...xlsx` | Cinco proyectos de edificios (centro de costos = código): fases, apartamentos, infraestructura, niveles y materiales |
| `PROYECTOS NEXT-WAVE...xlsx` | Control de materiales por proyecto (reemplaza al resumen del control de proyectos) |
| `GASTOS PROYECTO FTTH...xlsx` | Compras con IVA, caja chica y viáticos con saldo por fondo (se enlazan al proyecto por centro de costos) |
| `PRECIARIO FINAL...xlsx` | Precios target, órdenes por nodo y códigos ERP del catálogo |

Orden recomendado: primero el control FTTH, luego los demás. Los Excel con datos del negocio **no se suben al repositorio**.

No se cargan las hojas de personal (DUI y teléfonos), los balances por brigada (`E-C`, `H-B`, `O-P`) ni los BOM por nodo.
