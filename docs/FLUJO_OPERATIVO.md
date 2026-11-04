# Flujo operativo por nodo (proyectos FTTH)

> Fuente: indicaciones de Bryan Calderón del 2026-11-04 y el Excel `PROYECTO_FTTH_2026_61.xlsx`.
> Lo de **"Decisiones confirmadas"** es definitivo. Lo de **"Pendiente por confirmar"** es
> interpretación y no se implementa hasta que Bryan lo confirme (regla 8 del plan).

## Etapas del nodo

Las etapas **no tienen un orden obligatorio**: a veces se salta la secuencia y el resultado es el
mismo. El sistema las muestra en este orden de referencia, pero cada una se registra por separado.

| # | Etapa | Detalle |
|---|---|---|
| Previo | Supervisión de CAES | Solo se supervisa en campo. **No afecta el flujo.** |
| 1 | Replanteo del nodo | Junto a un supervisor, por nodo. |
| 2 | Entrega de materiales | Por nodo, a la brigada contratista. |
| 3 | Instalación de cable FC | 14 largos: 5, 50, 80, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450 y 500 m. |
| 4 | Instalación de cable MPO | 9 largos: 100, 150, 200, 250, 300, 350, 400, 450 y 500 m. |
| 5 | Instalación de FAT | Sub-box y end-box. |
| 6 | Instalación de Hub-box y X-box | |
| 7 | Fusiones en X-box | |
| 8 | Cruces americanos | **Parte de la ODN.** Cantidad proyectada según el diseño; puede cambiar. |
| 9 | Mediciones de FATs | Se miden las FATs para saber si les llega la potencia correcta. |
| 10 | Correcciones estéticas y técnicas | Las que salen de las mediciones y la revisión. |
| 11 | **ATP** | Documento formal que se entrega al cliente. Se adjunta firmado. |
| 12 | Recepción final | |
| Cierre | **ODN finalizada** | Se marca **a mano**. |

## Feeder (troncal): dos niveles

1. **Avance global del feeder (todo el proyecto).** Metros proyectados e instalados por tipo de
   fibra (12H, 24H, 48H y 96H), más cierres y ODF. Hoy: 41 000 m proyectados, 15 052 m instalados
   (37 %). Sirve para ver cuánto del feeder está construido.
2. **Estado del feeder por nodo.** Tres opciones: **PENDIENTE, EN PROCESO, FINALIZADO.** Solo sirve
   para saber si el feeder ya pasó por la construcción de la ODN de ese nodo.

## Decisiones confirmadas

1. **Sin orden obligatorio.** Las etapas se pueden registrar en cualquier orden, sin bloqueos ni avisos.
2. **CAES no condiciona nada.** Es una supervisión de campo y aparece como casilla informativa.
3. **Cruces americanos son parte de la ODN.** La cantidad **proyectada viene del diseño**, pero
   suele cambiar (suma o resta). Cada ajuste queda registrado con fecha, usuario y motivo, y se
   compara proyectado vs. ejecutado igual que el resto de las partidas.
4. **"ODN finalizada" se marca manualmente.**
5. **Estado de feeder por nodo, manual:** PENDIENTE, EN PROCESO o FINALIZADO.
6. **Los cierres de distribución y de continuidad vienen de la proyección de diseño.**
7. **Las etapas pesan por tiempo, no por valor.** Avance del nodo = días de las etapas finalizadas
   ÷ días totales. Los días de cada etapa se configuran en la plantilla del proyecto.
8. **"Cable SFC" es el cable FC** (SC/APC) del catálogo.
9. **Documentos firmados.** Cada nodo tiene una sección para adjuntar documentos firmados
   (por ejemplo, el ATP y el de CAES), con fecha y usuario que los subió.
10. **Estados de las etapas:** Pendiente, En proceso y Finalizado. Las correcciones estéticas y
    técnicas también admiten "En corrección" y "N/A", como en el Excel.
11. **Nodo listo para entregar al cliente** = **ODN finalizada** y **feeder FINALIZADO**. El tablero
    muestra cuántos nodos están listos del total.
12. **Catálogo de largos.** Cable FC: 14 largos (incluye 80 m). Cable MPO: 9 largos (incluye 250 y
    350 m). Cada largo es un material distinto.
13. **ATP = entregable formal al cliente.** Va junto con las fusiones y las mediciones. Es una
    etapa del nodo y su documento se adjunta firmado.
14. **Mediciones de FATs.** Se miden para confirmar que llega la potencia correcta. Se registran
    **dos cosas**: el estado de las mediciones del nodo (Sin iniciar, En proceso, Finalizadas) y,
    **por cada FAT medida, la potencia en dBm y si es aceptable o no.** Regla tomada del Excel: el
    nodo está "listo para medir" cuando tiene el **90 %** de sus FATs instaladas (porcentaje
    configurable). Cuando las mediciones se finalizan, queda "medido".
15. **Aro de sujeción y trébol son materiales separados.** En el Excel los tréboles venían escritos
    en el comentario de la celda del aro (por ejemplo, SON19: 54 aros y 222 tréboles). Al importar
    se pasan a su propio material.
16. **Control de bitácora del supervisor.** El estado "ACTUALIZADO" del Excel solo indica si Bryan ya
    tomó la información de las bitácoras de los supervisores. En el sistema es una marca por nodo
    para saber si hay bitácora pendiente de actualizar. **Bryan pasa la información él mismo**: los
    supervisores no cargan nada en el sistema por ahora.
17. **Bodega no tiene sus archivos disponibles todavía.** Catálogo de ingresos, hoja de entrega e
    inventario los maneja bodega.

## Cómo lo modela el sistema

1. **Cada nodo tiene su lista de etapas**, con estado, fecha plan y real, quién la registró y
   observaciones. Es la base del avance por nodo y del indicador de ritmo.
2. **Avance ponderado por días planificados** (decisión 7). Mientras una etapa está En proceso no
   suma avance; suma cuando pasa a Finalizado.
3. **El catálogo distingue cada largo de cable** (14 de FC y 9 de MPO, decisión 12).
4. **Lo instalado se registra por día** (fecha, brigada, cantidad) y alimenta la liquidación: FC,
   MPO, FAT, Hub-box, X-box, fusiones y cruces americanos. Así "Instalado" no se captura dos veces.
5. **Mediciones por FAT:** cada FAT medida guarda fecha, potencia en dBm y resultado (aceptable o
   no). El nodo muestra cuántas FATs están medidas, cuántas son aceptables y cuántas no, y el
   resultado puede exportarse junto con el ATP.
6. **Cruces americanos con ajustes:** cantidad original del diseño, ajustes con motivo y cantidad
   ejecutada. Total vigente = original ± ajustes. Nada se borra (principio 3).
7. **Alias:** sub-box = NAP de paso y end-box = NAP final.
8. **Feeder:** tabla del proyecto (metros por tipo de fibra, cierres, ODF) y un estado por nodo.
9. **Ayuda con cierres:** si la proyección de un nodo no trae cierres de distribución ni de
   continuidad, el sistema muestra el aviso "el feeder ya pasó por este nodo". Es solo una ayuda: el
   estado lo sigue poniendo Bryan.
10. **Bitácora:** cada cambio de estado o de cantidad queda en auditoría (quién, cuándo, motivo).

## Dónde entra en el plan de sprints

- **Sprint 1:** importar AVANCE ODN y AVANCE FEEDER del Excel; plantilla de etapas con días por
  etapa; catálogo con los largos de FC y MPO, aro y trébol; estado de feeder por nodo; casilla de
  CAES; marca manual de ODN finalizada; marca de bitácora pendiente.
- **Sprint 3:** bodega (ingresos, entregas, inventario), instalado por día, cruces con ajustes, medición por FAT (dBm y resultado),
  cierres desde la proyección, mediciones y liquidación. Depende de los archivos de bodega o de
  empezar a capturar desde cero.
- **Sprint 5:** Gantt, avance por nodo ponderado por días e indicador de nodos listos para entregar.
- **Sprint 6:** avisos de etapas atrasadas.
- **Sprint 7:** ATP con registro fotográfico y exporte, y adjuntos de documentos firmados.

## Pendiente por confirmar

1. **Rango aceptable de potencia (dBm).** Mientras no se defina, "aceptable" se marca a mano en cada
   FAT. Si Bryan indica el rango (por ejemplo, mínimo y máximo en dBm), el sistema lo calcula solo
   y marca las que quedan fuera.
