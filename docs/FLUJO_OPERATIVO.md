# Flujo operativo por nodo (proyectos FTTH)

> Fuente: indicaciones de Bryan Calderón del 2026-11-04. Lo de **"Decisiones confirmadas"** es
> definitivo. Lo de **"Pendiente por confirmar"** es interpretación y no se implementa hasta que
> Bryan lo confirme (regla 8 del plan: no inventar reglas de negocio).

## Etapas de la ODN por nodo

Las etapas **no tienen un orden obligatorio**: a veces se salta la secuencia y el resultado es el
mismo. El sistema las muestra en este orden de referencia, pero cada una se registra por separado.

| # | Etapa | Detalle |
|---|---|---|
| Previo | Supervisión de CAES | Solo se supervisa en campo. **No afecta el flujo.** |
| 1 | Replanteo del nodo | Junto a un supervisor, por nodo. |
| 2 | Entrega de materiales | Por nodo, a la brigada contratista. |
| 3 | Instalación de cable FC | Largos: 5, 50, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450 y 500 m. |
| 4 | Instalación de cable MPO | Largos: 100, 150, 200, 250, 300, 350, 400, 450 y 500 m. |
| 5 | Instalación de FAT | Sub-box y end-box. |
| 6 | Instalación de Hub-box y X-box | |
| 7 | Fusiones en X-box | |
| 8 | Cruces americanos | **Parte de la ODN.** Cantidad proyectada según el diseño; puede cambiar. |
| Cierre | **ODN finalizada** | Se marca **a mano**. |

## Feeder (troncal) por nodo

Cada nodo tiene un estado de feeder con tres opciones: **PENDIENTE, EN PROCESO, FINALIZADO**.
Sirve para saber si la fibra del feeder (también llamado troncal) ya pasó por ese nodo y, con eso,
cuánto está listo para entregar al cliente.

## Decisiones confirmadas

1. **Sin orden obligatorio.** Las etapas se pueden registrar en cualquier orden. El sistema no
   bloquea ni da avisos por saltarse la secuencia.
2. **CAES no condiciona nada.** Es una supervisión de campo y aparece como casilla informativa.
3. **Cruces americanos son parte de la ODN.** Se agregan siempre como una etapa más del nodo.
   La cantidad **proyectada viene del diseño de construcción**, pero suele sufrir alteraciones
   (suma o resta). Por eso la cantidad proyectada se puede **ajustar**, y cada ajuste queda
   registrado con fecha, usuario y motivo. Se compara proyectado vs. ejecutado igual que el resto
   de las partidas.
4. **"ODN finalizada" se marca manualmente.** El sistema no la calcula ni la marca solo.
5. **Estado de feeder por nodo, manual:** PENDIENTE, EN PROCESO o FINALIZADO. Es un dato aparte de
   las etapas de la ODN.
6. **Los cierres de distribución y de continuidad vienen de la proyección de diseño.**
7. **Las etapas pesan por tiempo, no por valor.** Algunas tardan más. El avance del nodo se calcula
   con los días planificados de cada etapa: avance = días de las etapas finalizadas ÷ días totales.
   Los días de cada etapa son configurables en la plantilla del proyecto.
8. **"Cable SFC" es el cable FC** (SC/APC) del catálogo.
9. **Documentos firmados.** Cada nodo tiene una sección para adjuntar documentos firmados
   (por ejemplo, el de supervisión de CAES), con fecha y usuario que los subió.
10. **Los estados de las etapas** usan las mismas tres opciones: Pendiente, En proceso y Finalizado.

## Cómo lo modela el sistema

1. **Cada nodo tiene su lista de etapas**, con estado, fecha plan y real, quién la registró y
   observaciones. Es la base del avance por nodo y del indicador de ritmo.
2. **Avance ponderado por días planificados** (decisión 7). Mientras una etapa está En proceso no
   suma avance; suma cuando pasa a Finalizado.
3. **El catálogo distingue cada largo de cable:** 13 de FC y 9 de MPO son materiales distintos
   (como en el diseño: "Cable FC SC/APC 50 m", "Cable MPO 400 m").
4. **Lo instalado alimenta la liquidación:** FC (etapa 3), MPO (4), FAT (5), Hub-box y X-box (6) y
   cruces americanos (8). Así "Instalado" no se captura dos veces.
5. **Cruces americanos con ajustes:** cantidad proyectada original (del diseño), ajustes con motivo
   y cantidad ejecutada. El total proyectado vigente es original ± ajustes. Nada se borra
   (principio 3).
6. **Alias:** sub-box = NAP de paso y end-box = NAP final.
7. **Ayuda con cierres:** si la proyección de un nodo no trae cierres de distribución ni de
   continuidad, el sistema muestra el aviso "el feeder ya pasó por este nodo". Es solo una ayuda: el
   estado del feeder lo sigue poniendo Bryan.
8. **Bitácora:** cada cambio de estado o de cantidad queda en auditoría (quién, cuándo, motivo).

## Dónde entra en el plan de sprints

- **Sprint 1:** plantilla de etapas con días por etapa (incluye cruces americanos), casilla de CAES,
  marca manual de ODN finalizada, estado de feeder por nodo, catálogo con los largos de FC y MPO.
- **Sprint 3:** entrega de materiales por nodo, instalado por etapa, cruces americanos con ajustes,
  cierres desde la proyección y liquidación.
- **Sprint 5:** Gantt, avance por nodo ponderado por días e indicador de nodos listos para entregar.
- **Sprint 6:** avisos de etapas atrasadas.
- **Sprint 7:** adjuntos de documentos firmados por nodo (si no se adelantan antes).

## Pendiente por confirmar

1. **"Listo para entregar al cliente":** propongo que un nodo cuente como listo cuando tiene
   **ODN finalizada y feeder FINALIZADO**. El tablero mostraría cuántos nodos están listos del
   total. ¿Es esa la regla, o falta alguna condición?
