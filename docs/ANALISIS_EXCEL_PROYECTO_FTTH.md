# Análisis del Excel `PROYECTO_FTTH_2026_61.xlsx`

Archivo: `docs/excel-origen/PROYECTO_FTTH_2026_61.xlsx` · Proyecto: **FTTH Nextwave 2601-2607 · Sonsonate**
Analizado el 2026-11-04. Las cifras de abajo las recalculé desde los datos crudos, porque muchas
fórmulas del archivo están rotas (ver "Problemas detectados").

## Qué trae el archivo

| Hoja | Qué contiene | Estado |
|---|---|---|
| RESUMEN GENERAL | Tablero: avance global, avance por nodo, por categoría, por longitud, entregas a brigadas, inventario | **Roto.** 213 de 415 fórmulas dan `#REF!` y el avance global muestra 0 %. |
| AVANCE ODN | Una fila por nodo (20 nodos) y 130 columnas con todo el control de la ODN | Datos buenos. 36 fórmulas rotas, entre ellas los metros de FO y MPO. |
| AVANCE FEEDER | Feeder global del proyecto (no por nodo): metros, materiales y cierres | Datos buenos. 27 fórmulas rotas (entregado y saldo). |

**Faltan hojas que el archivo cita y ya no existen:** catálogo/ingresos, hoja de entrega a brigadas,
inventario de bodega, proyección de diseño y seriales. Por eso las fórmulas dan `#REF!`.

## Hoja AVANCE ODN (columnas)

| Bloque | Columnas | Contenido |
|---|---|---|
| Generales | B-L | Sitio, estado de datos, nodo, responsable de brigada, teléfono, fechas inicio y fin, latitud y longitud, operador (NEXTWAVE), supervisor |
| Cable FO preconectorizado | M-BD | 14 largos (5, 50, 80, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450, 500 m), cada uno con proyectado, instalado y pendiente; avance % y metros |
| Cable MPO | BE-CA | 7 largos (100, 150, 200, 300, 400, 450, 500 m); avance % y metros |
| Materiales de ODN | CB-DA | Conectores, cinta Band-It, hebillas, abrazaderas, pernos, herrajes de tensión, temclan, aro de sujeción, fibra muerta, aletas, soportes, cinchos, cinta aislante, brazos, poste nuevo, viñetas. **Una sola cantidad (usada), sin proyectado.** |
| Cruces americanos | CY-CZ | Proyectado y ejecutado |
| FATs | DB-DL | Sub-box, end-box, HB-box y X-box (proyectado e instalado), total y % de avance |
| Fusión en X-box | DM-DP | Contratista de fusión, fusiones proyectadas y reales, mangas termocontráctiles |
| Cierre del nodo | DQ-DU | Mediciones, "¿listo para medir?", correcciones estéticas, técnicas y recepción final |
| Estado | DV-DZ | Avance global, ATP, comentario de pago, observaciones, requerimientos |

### Reglas que el Excel ya aplica

- **ATP:** `SIN INICIAR`, `EN PROCESO`, `FINALIZADO`.
- **Mediciones:** `SIN INICIAR`, `EN PROCESO`, `FINALIZADAS`.
- **Estéticas y técnicas:** `EN CORRECCION`, `FINALIZADAS`, `N/A`.
- **¿Listo para medir?:** `LISTO` cuando el avance de FATs llega al **90 %** (umbral guardado en la celda DR4); `MEDIDO` cuando las mediciones están finalizadas; si no, `PENDIENTE`.
- **Avance global del nodo:** suma de unidades instaladas (FO, MPO, FATs, fusiones, cruces, más 1 por mediciones finalizadas) dividida entre las proyectadas. El cruce usa el mayor entre proyectado y ejecutado.

## Hoja AVANCE FEEDER

Una sola fila para todo el proyecto, con una sola brigada (Optical Prescision).

| Concepto | Proyectado | Instalado | Avance |
|---|---|---|---|
| Feeder total | 41 000 m | 15 052 m | 36,7 % |
| FO 12H | 12 000 m | 1 025 m | |
| FO 24H | 11 000 m | 3 027 m | |
| FO 48H | 6 000 m | 4 000 m | |
| FO 96H | 12 000 m | 7 000 m | |
| Cierres (12H, 24H, 48H, 96H) | 14 | 2 (ambos de 96H) | 14,3 % |
| ODF 96H | 3 | 1 | 33,3 % |

Además lleva los materiales del feeder (abrazaderas, pernos, herrajes, guardacabos, pigtail,
coupling, patch cord) para comparar **entregado vs. utilizado**; el "entregado" venía de la hoja
de entrega que ya no está.

## Cifras recalculadas del proyecto (20 nodos)

| Concepto | Proyectado | Instalado | Avance |
|---|---|---|---|
| Cable FO (unidades) | 886 | 535 | 60,4 % |
| Cable FO (metros) | 113 025 | 67 910 | 60,1 % |
| Cable MPO (unidades) | 76 | 37 | 48,7 % |
| Cable MPO (metros) | 24 200 | 11 800 | 48,8 % |
| Sub-box | 664 | 211 | 31,8 % |
| End-box | 206 | 61 | 29,6 % |
| HB-box | 66 | 19 | 28,8 % |
| X-box | 18 | 5 | 27,8 % |
| FATs en total | 954 | 296 | 31,0 % |
| Fusiones en X-box | 146 | 76 | 52,1 % |
| Cruces americanos | 31 | 16 | |

Nodos: **3 finalizados** (SON19, SON22, SON23, con ATP y mediciones finalizadas), **9 en proceso**
y **8 sin iniciar** (SON11, SON08, SON17, SON18, SON14, SON16, SON10, SON06). La fecha final de los
nodos en proceso es el 30 o 31 de octubre de 2026, ya vencida.

## Problemas detectados

1. **El resumen no funciona.** Todas sus fórmulas apuntan a hojas borradas.
2. **Metros de FO y MPO rotos** en todos los nodos (columnas BD y CA).
3. **SON10 y SON06 quedan fuera** de los totales y del resumen: las sumas llegan solo hasta la fila 23.
4. **Lo instalado se escribe como suma a mano** (53 celdas del tipo `=4+5+1+6+2+1`). Se pierde la
   fecha, la brigada y el detalle de cada entrega diaria. El sistema lo registrará día por día.
5. **Fechas mezcladas con texto:** 8 nodos tienen "POR DEFINIR" en las columnas de fecha.
6. **Mangas termocontráctiles** tiene fórmulas distintas por fila (a veces igual a las fusiones,
   a veces un porcentaje). Su total no tiene sentido.
7. **El avance global cuenta unidades, no tiempo,** y suma 1 por el solo hecho de que el nodo exista.
   Eso choca con la regla acordada (avance ponderado por días de cada etapa).
8. **Datos escondidos en comentarios de celda:** en "aro de sujeción" hay notas como "222 TREBOLES",
   y en "fibra muerta" una nota de que el contratista puso 31 m adicionales a los entregados.
9. **Datos repetidos y con errores de escritura:** el teléfono de cada brigada se repite en cada
   nodo, "Optical Prescision" está mal escrito en dos hojas y hay encabezados con espacios dobles.
10. **Materiales de ODN sin proyectado:** no se puede medir variación contra el diseño.
11. **Cruces ejecutados sin proyección:** 9 cruces en SON19, SON22, SON23 y SON21 se hicieron sin
    estar proyectados. Confirma lo que dijiste: las cantidades cambian respecto al diseño.
12. **SON17 y SON18** solo traen 1 FAT proyectado, parece un valor provisional.

## Diferencias con lo que me indicaste

- **FO 80 m** existe en el Excel y no estaba en tu lista. **MPO de 250 y 350 m** estaban en tu lista
  y el Excel no los trae.
- La tabla "Fibra ODN por longitud" del resumen lista largos distintos a los de la hoja ODN.
- El Excel agrupa **sub-box, end-box, HB-box y X-box como "FATs"**; tu flujo los separa.
- El Excel tiene etapas **posteriores a las fusiones** que no están en tu flujo: ATP, mediciones,
  correcciones estéticas y técnicas, y recepción final.
- El Excel **no tiene** replanteo, CAES, entrega de materiales por etapa ni **estado de feeder por nodo**.
  El feeder del Excel es global y en metros.

## Cómo se traduce al sistema

| En el Excel | En el sistema |
|---|---|
| Fila de nodo (B-L) | **Sitio** con coordenadas, supervisor y brigada; la brigada pasa a ser un **Contratista** único |
| FO y MPO por largo | Un **material por largo** con proyectado (diseño) e instalado por día |
| Materiales de ODN | Materiales con **movimiento de instalado**; el proyectado llega con la proyección de diseño |
| Cruces americanos | Partida con **proyectado, ajustes con motivo y ejecutado** |
| FATs, fusiones | Etapas del nodo con cantidades proyectadas e instaladas |
| ATP, mediciones, correcciones, recepción | **Etapas finales del nodo** (a confirmar) con los estados del Excel |
| Feeder global | **Feeder del proyecto** (metros por tipo de fibra, cierres, ODF) más el **estado por nodo** de tu flujo |
| Comentarios de celda | **Notas** de la partida o del nodo |
| Hoja de entrega, catálogo, inventario | Movimientos de bodega (Sprint 3); falta el archivo para importarlos |

## Pendiente por confirmar

1. Enviar los archivos que faltan: **catálogo e ingresos, hoja de entrega, inventario, proyección de
   diseño y seriales.**
2. ¿FO de **80 m** es válido? ¿Existen MPO de **250 y 350 m**?
3. ¿ATP, mediciones, correcciones estéticas y técnicas, y recepción final son **etapas del flujo**
   después de las fusiones?
4. ¿Qué significa **ACTUALIZADO** en "DATA STATUS"? ¿Y qué es un **trébol** en las notas del aro de sujeción?
5. ¿El **feeder global en metros** y el **estado de feeder por nodo** conviven como dos datos distintos?
