import html, pathlib
D=pathlib.Path(__file__).resolve().parent
def code(s): return '<pre>'+html.escape(s.strip('\n'))+'</pre>'
def table(head, rows, cls=''):
    h=''.join(f'<th>{c}</th>' for c in head)
    b=''.join('<tr>'+''.join(f'<td>{c}</td>' for c in r)+'</tr>' for r in rows)
    return f'<table class="{cls}"><thead><tr>{h}</tr></thead><tbody>{b}</tbody></table>'

body=[]
A=body.append
A('<h1>Guía: Dashboard de Control del Despliegue FTTH en Power BI</h1><p class="meta">Nov 10, 2026 · TEK-COM · mismo diseño de la guía de producción, con los datos del proyecto FTTH</p>')
A('<h2>Resumen y archivos</h2><p>Todo va en <b>una sola página de 1280 × 720 px</b> (16:9), sin desplazamiento. La imagen de fondo dibuja la barra lateral, los paneles blancos, las tarjetas y los íconos. Encima colocas visuales con transparencia, cada uno con tamaño y posición exactos. Es la misma estructura de la guía del tablero de producción: lo único que cambia son los datos, las medidas y los gráficos.</p>')
A(table(['Archivo','Para qué sirve','Paso'],[
 ['fondo_dashboard_despliegue_ftth.png','Fondo del lienzo, con el diseño completo (2560 × 1440)','3'],
 ['tema_control_despliegue_ftth.json','Colores, fuentes y visuales sin fondo ni borde (el mismo tema rojo, negro y gris)','3'],
 ['DATOS_POWER_BI_FTTH.xlsx','Datos del sistema con la estructura exacta del modelo. Se descarga del sistema (Presentación › <b>Datos para Power BI</b>) cada vez que quieras actualizar','0 y 1'],
]))
A('<p><b>Cómo poner la posición exacta de un visual:</b> selecciónalo, abre <b>Formato (pincel) › General › Propiedades</b> y escribe <b>Tamaño</b> (Alto, Ancho) y <b>Posición</b> (Horizontal = X, Vertical = Y). Las medidas se cuentan desde la esquina superior izquierda.</p>')
A(table(['Zona del lienzo','X','Y','Ancho','Alto'],[
 ['Barra lateral de filtros','0','0','200','720'],['Encabezado y fecha de actualización','216','12','1048','46'],['Fila de 6 tarjetas KPI','216','66','1048','84'],
 ['Fila 1 de gráficos (4 paneles)','216','160','1048','214'],['Fila 2 de gráficos (4 paneles)','216','384','1048','196'],['Franja de insights (5 tarjetas)','216','588','1048','124']]))
A(f'<img src="file://{D}/fondo_dashboard_despliegue_ftth.png" class="bg"><p>Así queda el fondo antes de poner los visuales: cada caja blanca o de color es el lugar exacto de un visual. El logo de TEK-COM ya viene en la barra lateral.</p>')

A('<h2>Paso 0 — Obtener los datos del sistema</h2><ol><li>Entra al sistema de proyectos, abre <b>Presentación</b> y toca el botón <b>Datos para Power BI</b> (arriba a la derecha). Se descarga <b>DATOS_POWER_BI_FTTH.xlsx</b>.</li><li>Guárdalo en la carpeta de <b>SharePoint u OneDrive de TEK-COM</b> (no en Descargas). Así Power BI en la nube puede leerlo y refrescarlo solo.</li><li>Para actualizar los datos: descarga el archivo de nuevo desde el sistema y reemplaza el que está en SharePoint/OneDrive (mismo nombre). Los números del sistema y los del tablero salen de la misma fuente.</li></ol>')

A('<h2>Paso 1 — Datos y modelo</h2><p>El modelo es una <b>estrella</b>: 4 tablas de hechos y 3 dimensiones que filtran a las cuatro al mismo tiempo. Así cada filtro de la barra lateral mueve todos los gráficos.</p>')
A(table(['Tabla','Columnas','Tipo de dato'],[
 ['fInstalado','Fecha, Nodo, Brigada, Supervisor, Estado, Metros, FATs, Piezas','Fecha; texto; decimales (lo instalado por día y nodo)'],
 ['fNodos','Nodo, Brigada, Supervisor, Estado, EstadoOrden, AvancePct, FATsInstaladas, FATsProyectadas, CableMetros, Piezas, HP, XBoxFusionada, ListoParaMedir, ListoParaEntregar, FinPlan','Texto; enteros; decimales; fecha (una fila por nodo)'],
 ['fPartidas','Nodo, Brigada, Supervisor, Estado, Partida, OrdenPartida, Proyectado, Instalado','Texto; enteros; decimales (cable FC, MPO, sub-box, end-box, hub-box, x-box, fusiones y cruces)'],
 ['fEtapas','Nodo, Brigada, Supervisor, Estado, Etapa, OrdenEtapa, EstadoEtapa, Finalizada','Texto; enteros (una fila por nodo y etapa)'],
 ['dBrigada','Brigada','Texto'],['dSupervisor','Supervisor','Texto'],['dEstado','Estado, Orden','Texto, entero (Finalizado, En avance, Sin iniciar)'],
 ['tInfo','Proyecto, Corte','Texto, fecha (fecha de corte del archivo)'],
]))
A('<ol><li><b>Inicio › Obtener datos › Libro de Excel</b> y elige DATOS_POWER_BI_FTTH.xlsx. Marca las <b>8 tablas</b> (no marques la hoja Leeme) y dale <b>Transformar datos</b>. Si lo guardaste en SharePoint/OneDrive, usa <b>Obtener datos › Web</b> con el enlace del archivo para que se actualice solo.</li><li>En Power Query revisa los tipos: las fechas como <b>Fecha</b>, las cantidades como <b>Número decimal</b> y los 0/1 como <b>Número entero</b>. Luego <b>Cerrar y aplicar</b>.</li><li><b>Modelado › Nueva tabla</b> y pega el calendario:</li></ol>')
A(code('''
dCalendario =
VAR _ini = DATE ( YEAR ( MIN ( fInstalado[Fecha] ) ), 1, 1 )
VAR _fin = DATE ( YEAR ( MAX ( fInstalado[Fecha] ) ), 12, 31 )
RETURN
    SELECTCOLUMNS (
        CALENDAR ( _ini, _fin ),
        "Fecha", [Date],
        "Año", YEAR ( [Date] ),
        "MesNum", MONTH ( [Date] ),
        "Mes", SWITCH ( MONTH ( [Date] ), 1, "Enero", 2, "Febrero", 3, "Marzo", 4, "Abril", 5, "Mayo", 6, "Junio", 7, "Julio", 8, "Agosto", 9, "Septiembre", 10, "Octubre", 11, "Noviembre", 12, "Diciembre" ),
        "MesCorto", SWITCH ( MONTH ( [Date] ), 1, "ene", 2, "feb", 3, "mar", 4, "abr", 5, "may", 6, "jun", 7, "jul", 8, "ago", 9, "sep", 10, "oct", 11, "nov", 12, "dic" )
    )
'''))
A('<ol start="4"><li>Con dCalendario seleccionada: <b>Herramientas de tabla › Marcar como tabla de fechas</b> y elige la columna Fecha.</li><li>Ordena para que meses, estados, partidas y etapas no salgan en orden alfabético (<b>Herramientas de columna › Ordenar por columna</b>):<ul><li>dCalendario[MesCorto] y dCalendario[Mes] por <b>MesNum</b></li><li>dEstado[Estado] por <b>Orden</b> (Finalizado, En avance, Sin iniciar)</li><li>fPartidas[Partida] por <b>OrdenPartida</b></li><li>fEtapas[Etapa] por <b>OrdenEtapa</b></li></ul></li><li>En la vista de modelo deja exactamente estas relaciones, todas <b>Uno a varios</b> con dirección de filtro <b>Única</b>:</li></ol>')
A(table(['Desde (lado 1)','Hacia (lado varios)'],[
 ['dCalendario[Fecha]','fInstalado[Fecha]'],
 ['dBrigada[Brigada]','fNodos[Brigada], fInstalado[Brigada], fPartidas[Brigada] y fEtapas[Brigada]'],
 ['dSupervisor[Supervisor]','fNodos[Supervisor], fInstalado[Supervisor], fPartidas[Supervisor] y fEtapas[Supervisor]'],
 ['dEstado[Estado]','fNodos[Estado], fInstalado[Estado], fPartidas[Estado] y fEtapas[Estado]']]))
A('<p>Si Power BI creó solo alguna relación entre tablas de hechos (por ejemplo fNodos con fPartidas por Nodo), elimínala: las tablas de hechos solo se conectan por las dimensiones. Son 13 relaciones en total. <b>Nota:</b> los filtros Año y Mes mueven solo los gráficos de producción (los que salen de fInstalado); el resto es el estado actual de los nodos.</p>')

A('<h2>Paso 2 — Medidas DAX</h2><p>Los nombres de los KPI son exactamente los textos que muestran las tarjetas: escríbelos igual, con tildes.</p><ol><li><b>Inicio › Especificar datos</b>, nombre <b>_Medidas</b>, <b>Cargar</b>.</li><li>Para cada medida: selecciona _Medidas, <b>Modelado › Nueva medida</b>, pega el código y pon el formato en <b>Herramientas de medida</b>.</li><li>Al terminar, borra la columna “Columna1” de _Medidas.</li></ol><h3>Medidas base</h3>')
A(code('''
Nodos = COUNTROWS ( fNodos )
Nodos Finalizados = CALCULATE ( COUNTROWS ( fNodos ), fNodos[EstadoOrden] = 1 )
FATs Proyectadas = SUM ( fNodos[FATsProyectadas] )
HP Finalizados = CALCULATE ( SUM ( fNodos[HP] ), fNodos[EstadoOrden] = 1 )
Metros Período = SUM ( fInstalado[Metros] )
FATs Período = SUM ( fInstalado[FATs] )
Piezas Período = SUM ( fInstalado[Piezas] )
'''))
A('<h3>KPI y apoyo</h3>')
A(code('''
Avance físico = DIVIDE ( AVERAGE ( fNodos[AvancePct] ), 100 )
Fibra tendida (m) = SUM ( fNodos[CableMetros] )
FATs instaladas = SUM ( fNodos[FATsInstaladas] )
Piezas instaladas = SUM ( fNodos[Piezas] )
Home passes (HP) = SUM ( fNodos[HP] )
X-box fusionada = SUM ( fNodos[XBoxFusionada] )

FATs por instalar = MAX ( 0, [FATs Proyectadas] - [FATs instaladas] )
% FATs = DIVIDE ( [FATs instaladas], [FATs Proyectadas] )
HP por habilitar = [Home passes (HP)] - [HP Finalizados]
Listos para medir = SUM ( fNodos[ListoParaMedir] )
% Avance partida = DIVIDE ( SUM ( fPartidas[Instalado] ), SUM ( fPartidas[Proyectado] ) )
% Etapa finalizada = DIVIDE ( SUM ( fEtapas[Finalizada] ), COUNTROWS ( fEtapas ) )
FATs acumuladas =
VAR _f = MAX ( dCalendario[Fecha] )
RETURN CALCULATE ( [FATs Período], FILTER ( ALLSELECTED ( dCalendario ), dCalendario[Fecha] <= _f ) )
Avance acumulado de FATs = DIVIDE ( [FATs acumuladas], [FATs Proyectadas] )
Fecha Actualización = "Actualizado al " & FORMAT ( MAX ( tInfo[Corte] ), "dd/mm/yyyy" )
'''))
A(table(['Medida','Formato'],[
 ['Fibra tendida (m), FATs instaladas, Piezas instaladas, Home passes (HP), X-box fusionada, FATs por instalar, HP por habilitar, Listos para medir, Nodos, Nodos Finalizados, FATs Proyectadas, HP Finalizados, Metros Período, FATs Período, Piezas Período, FATs acumuladas','Número entero, separador de miles activado'],
 ['Avance físico, % FATs, % Avance partida, % Etapa finalizada, Avance acumulado de FATs','Porcentaje, 1 decimal']]))
A('<h3>Textos de la franja de insights</h3><p>Devuelven texto, no llevan formato.</p>')
A(code('''
Ins Nodo Valor =
VAR _t = FILTER ( fNodos, fNodos[EstadoOrden] = 2 )
RETURN IF ( COUNTROWS ( _t ) = 0, "—", MAXX ( TOPN ( 1, _t, fNodos[AvancePct], DESC ), fNodos[Nodo] ) )
Ins Nodo Sub =
VAR _t = FILTER ( fNodos, fNodos[EstadoOrden] = 2 )
VAR _n = TOPN ( 1, _t, fNodos[AvancePct], DESC )
RETURN IF ( COUNTROWS ( _t ) = 0, "Todos finalizados o sin iniciar",
    FORMAT ( MAXX ( _n, fNodos[AvancePct] ), "0.0" ) & "% · " & MAXX ( _n, fNodos[Brigada] ) )
Ins Nodo Ind = "● En obra"

Ins Brigada Valor =
VAR _t = FILTER ( ADDCOLUMNS ( VALUES ( dBrigada[Brigada] ), "@a", [Avance físico] ), NOT ISBLANK ( [@a] ) )
RETURN IF ( COUNTROWS ( _t ) = 0, "—", MAXX ( TOPN ( 1, _t, [@a], DESC ), dBrigada[Brigada] ) )
Ins Brigada Sub =
VAR _t = FILTER ( ADDCOLUMNS ( VALUES ( dBrigada[Brigada] ), "@a", [Avance físico] ), NOT ISBLANK ( [@a] ) )
RETURN IF ( COUNTROWS ( _t ) = 0, "Sin datos", FORMAT ( MAXX ( _t, [@a] ), "0.0%" ) & " de avance promedio" )
Ins Brigada Ind = "▲ Brigada líder"

Ins FATs Valor = FORMAT ( [FATs por instalar], "#,0" )
Ins FATs Sub = "de " & FORMAT ( [FATs Proyectadas], "#,0" ) & " proyectadas"
Ins FATs Ind = IF ( [% FATs] >= 0.9, "✔ ", "● " ) & FORMAT ( [% FATs], "0.0%" ) & " completado"

Ins Medir Valor = FORMAT ( [Listos para medir], "#,0" )
Ins Medir Sub =
VAR _l = CONCATENATEX ( FILTER ( fNodos, fNodos[ListoParaMedir] = 1 ), fNodos[Nodo], ", ", fNodos[Nodo] )
RETURN IF ( _l = "", "Ninguno todavía", _l )
Ins Medir Ind = IF ( [Listos para medir] > 0, "✔ Listos para medir potencia", "● Aún sin FATs suficientes" )

Ins HP Valor = FORMAT ( [HP por habilitar], "#,0" )
Ins HP Sub = "de " & FORMAT ( [Home passes (HP)], "#,0" ) & " HP"
Ins HP Ind = "● " & FORMAT ( DIVIDE ( [HP Finalizados], [Home passes (HP)] ), "0.0%" ) & " ya habilitado"
'''))

A('<h2>Paso 3 — Lienzo, fondo y tema</h2><ol><li>Haz clic en un espacio vacío del lienzo y abre <b>Formato (pincel)</b>. Debe decir <b>Formato de página</b>.</li><li><b>Configuración del lienzo › Tipo: 16:9</b>. Revisa que diga 1280 de ancho y 720 de alto. Alineación vertical: <b>Superior</b>.</li><li><b>Fondo del lienzo › Imagen › Examinar</b> y elige fondo_dashboard_despliegue_ftth.png. <b>Ajuste de imagen: Ajustar</b> y <b>Transparencia: 0 %</b>. Si se ve lavado, la transparencia no está en 0.</li><li><b>Papel tapiz › Color #1F2026</b>, transparencia 0 %.</li><li><b>Ver › Temas › Examinar temas</b> y elige tema_control_despliegue_ftth.json (en algunas versiones: <b>Diseño › Importar tema</b>).</li><li>En <b>Ver</b>, desactiva <b>Ajustar a la cuadrícula</b> para que los visuales no salten al moverlos.</li></ol>')
A(table(['Color','Código','Dónde se usa'],[['Rojo principal','#E3262F','Barras, línea, botón, Avance físico'],['Negro grafito','#1F2026','Barra lateral, 2 primeras tarjetas, textos'],['Gris claro','#C4C6CC','Series secundarias, “Sin iniciar”'],['Gris de texto secundario','#6B6E76','Etiquetas pequeñas'],['Verde (meta cumplida)','#2E9E5B','Indicadores positivos']]))

A('<h2>Paso 4 — Panel de filtros</h2><p>La barra lateral lleva 5 segmentaciones desplegables y un botón que borra todos los filtros. Los títulos (AÑO, MES, BRIGADA…) y las cajas blancas ya vienen en el fondo.</p>')
A(table(['Filtro','Campo','X','Y','Ancho','Alto'],[
 ['Año','dCalendario[Año]','16','134','168','32'],['Mes','dCalendario[MesCorto]','16','198','168','32'],['Brigada','dBrigada[Brigada]','16','262','168','32'],
 ['Supervisor','dSupervisor[Supervisor]','16','326','168','32'],['Estado del nodo','dEstado[Estado]','16','390','168','32'],['Botón Limpiar filtros','—','52','652','132','36']]))
A('<p><b>Formato de cada segmentación</b> (arma la primera completa y luego cópiala con Ctrl+C / Ctrl+V; solo cambias el campo y la posición):</p><ol><li><b>Configuración de segmentación › Opciones › Estilo: Lista desplegable</b>.</li><li><b>Selección:</b> activa <b>Opción “Seleccionar todo”</b> y deja apagada la selección única.</li><li><b>Encabezado del segmentador: Desactivado</b>. El título ya está en el fondo.</li><li><b>Valores:</b> fuente Segoe UI 9, color #1F2026, fondo #FFFFFF.</li><li><b>General › Efectos:</b> fondo y borde visual desactivados (el tema ya lo hace).</li></ol><p><b>Botón Limpiar filtros:</b> Insertar › Botones › En blanco, con la posición de la tabla. Texto: “LIMPIAR FILTROS”, Segoe UI Bold 9, blanco, centrado. Relleno #E3262F (al mantener el puntero #B81C24), sin borde, esquinas de 6 px. Acción: <b>Borrar todas las segmentaciones</b> (en Desktop se prueba con Ctrl + clic).</p>')

A('<h2>Paso 5 — Encabezado y tarjetas KPI</h2><p>El título, el subtítulo, las 6 cajas de color y sus íconos ya están en el fondo. Aquí solo colocas 7 tarjetas con los números. Usa la <b>tarjeta clásica</b> (la que en Formato tiene <b>Valor de llamada</b> y <b>Etiqueta de categoría</b>).</p>')
A(table(['Tarjeta','Medida','X','Y','Ancho','Alto'],[
 ['Fecha de actualización','Fecha Actualización','1116','18','144','32'],['KPI 1 (oscura)','Avance físico','220','96','160','50'],['KPI 2 (oscura)','Fibra tendida (m)','396','96','160','50'],
 ['KPI 3 (gris)','FATs instaladas','572','96','160','50'],['KPI 4 (gris)','Piezas instaladas','748','96','160','50'],['KPI 5 (gris)','Home passes (HP)','924','96','160','50'],['KPI 6 (gris)','X-box fusionada','1100','96','160','50']]))
A('<p><b>Formato de las 6 tarjetas KPI:</b> Valor de llamada Segoe UI Bold 20, color #FFFFFF en KPI 1 y 2 y #1F2026 en KPI 3 a 6; <b>Unidades de visualización: Ninguno</b> (si lo dejas en Automático verás “104 K” en lugar de 104,612); Etiqueta de categoría activada, Segoe UI 8, color #C9CBD0 en KPI 1 y 2 y #4A4C55 en KPI 3 a 6; <b>General › Título</b> desactivado. Tarjeta de fecha: Segoe UI Semibold 9, color #1F2026, sin etiqueta. Arma KPI 1, cópiala para KPI 2; arma KPI 3 y cópiala para 4, 5 y 6.</p>')

A('<h2>Paso 6 — Los 8 gráficos</h2><p>Cada gráfico ocupa exactamente su panel blanco del fondo. El título va en el propio visual, así lo puedes cambiar sin tocar la imagen.</p>')
A(table(['#','Título','Visual','Campos','X','Y','Ancho','Alto'],[
 ['1','Metros de fibra instalados por mes','Columnas agrupadas','Eje X: dCalendario[MesCorto] · Eje Y: Metros Período','216','160','312','214'],
 ['2','Avance acumulado de FATs','Áreas','Eje X: dCalendario[MesCorto] · Eje Y: Avance acumulado de FATs','538','160','248','214'],
 ['3','Avance por brigada','Barras agrupadas','Eje Y: dBrigada[Brigada] · Eje X: Avance físico','796','160','229','214'],
 ['4','Instalación por partida','Barras agrupadas','Eje Y: fPartidas[Partida] · Eje X: % Avance partida','1035','160','229','214'],
 ['5','Avance por nodo','Columnas agrupadas','Eje X: fNodos[Nodo] · Eje Y: Avance físico','216','384','312','196'],
 ['6','Nodos por estado','Anillos','Leyenda: dEstado[Estado] · Valores: Nodos','538','384','248','196'],
 ['7','Piezas instaladas por mes','Columnas agrupadas','Eje X: dCalendario[MesCorto] · Eje Y: Piezas Período','796','384','229','196'],
 ['8','Etapas finalizadas','Barras agrupadas','Eje Y: fEtapas[Etapa] · Eje X: % Etapa finalizada','1035','384','229','196']]))
A('<p><b>Formato común a los 8:</b> General › Título activado con el texto de la tabla, Segoe UI Semibold 11, #1F2026, alineado a la izquierda. Ejes con etiquetas Segoe UI 8, color #6B6E76 y títulos de ejes desactivados. Líneas de cuadrícula desactivadas. <b>Orden:</b> en las barras de brigadas y en el gráfico 5, menú … › Ordenar eje por la medida, <b>descendente</b>; en los gráficos por mes, por MesCorto <b>ascendente</b> (sale de ene a dic gracias al orden por MesNum); en las partidas y etapas, por su columna de orden, <b>ascendente</b>.</p>')
A('<p><b>Ajustes de cada uno:</b></p><ol><li><b>Metros por mes:</b> columnas #E3262F, etiquetas de datos activadas 7 pt, Espacio interior 20 %.</li><li><b>Avance acumulado de FATs:</b> línea #E3262F de 2 px, Sombrear área activado con transparencia 85 %, marcadores círculo de 5 px, etiquetas de datos 7 pt, Eje Y › Mínimo 0 y Máximo 1. En el panel Análisis agrega una <b>línea constante en 1</b> (100 %), gris #C4C6CC y discontinua.</li><li><b>Avance por brigada:</b> barras #E3262F, etiquetas de datos 8 pt, Eje X (valores) desactivado, Espacio interior 30 %.</li><li><b>Instalación por partida:</b> igual que el 3.</li><li><b>Avance por nodo:</b> columnas #E3262F, etiquetas de datos desactivadas (son muchos nodos), etiquetas del eje X a 6 pt, Espacio interior 20 %.</li><li><b>Nodos por estado:</b> colores Finalizado #E3262F, En avance #1F2026 y Sin iniciar #C4C6CC. Leyenda activada, arriba al centro. Etiquetas de detalle: contenido <b>Porcentaje del total</b>, posición <b>Afuera</b>, 8 pt. Forma › Radio interior 60 %.</li><li><b>Piezas instaladas por mes:</b> columnas #E3262F, etiquetas 7 pt, Eje Y desactivado, Espacio interior 25 %.</li><li><b>Etapas finalizadas:</b> igual que el 3.</li></ol>')

A('<h2>Paso 7 — Franja de insights</h2><p>Cada una de las 5 tarjetas del fondo lleva 3 tarjetas clásicas apiladas: el valor grande, una línea de contexto y un indicador. El título y el ícono ya están en el fondo.</p>')
A(table(['Tarjeta','Título (en el fondo)','X','Medida Valor','Medida Sub','Medida Ind'],[
 ['1','Nodo más avanzado en obra','230','Ins Nodo Valor','Ins Nodo Sub','Ins Nodo Ind'],['2','Brigada con mayor avance','438','Ins Brigada Valor','Ins Brigada Sub','Ins Brigada Ind'],
 ['3','FATs por instalar','646','Ins FATs Valor','Ins FATs Sub','Ins FATs Ind'],['4','Listos para medir','854','Ins Medir Valor','Ins Medir Sub','Ins Medir Ind'],['5','HP por habilitar','1062','Ins HP Valor','Ins HP Sub','Ins HP Ind']]))
A(table(['Fila','Y','Ancho','Alto','Formato del Valor de llamada'],[['Valor','638','188','30','Segoe UI Bold 15, #1F2026'],['Sub','668','188','18','Segoe UI 8, #6B6E76'],['Ind','686','188','18','Segoe UI Semibold 8, color #E3262F (tarjetas 1 y 2), #1F2026 (3, 4 y 5)']]))
A('<p>En las 15 tarjetas: <b>Etiqueta de categoría</b> y <b>Título</b> desactivados. Si algún texto se ve cortado, súbele 2 px al alto y baja 1 px la Y.</p>')

A('<h2>Paso 8 — Revisión final</h2><p>Los datos del sistema cambian cada día, así que el control es este: <b>compara el tablero con la pantalla Presentación del sistema</b> (con los filtros en “Todas/Todos”); salvo el avance, deben ser iguales. Con el archivo de ejemplo <b>DATOS_POWER_BI_FTTH_ejemplo.xlsx</b> (corte 10/11/2026) el tablero debe mostrar exactamente estos valores:</p>')
A(table(['Dónde','Valor esperado'],[
 ['Avance físico','34.9 % (promedio del avance de los 24 nodos; la Presentación del sistema, sin filtros, muestra el avance ponderado del plan de trabajo)'],
 ['Fibra tendida (m)','81,290'],['FATs instaladas','388 (de 1,188 proyectadas = 32.7 %)'],['Piezas instaladas','7,472'],['Home passes (HP)','26,031'],['X-box fusionada','9'],
 ['Nodos por estado','Finalizado 4, En avance 10, Sin iniciar 10'],['Nodo más avanzado en obra','SON05 · 92.5 % · Edwin Carcamo'],['Brigada con mayor avance','Optical Prescision · 100.0 %'],
 ['FATs por instalar','800 · de 1,188 proyectadas · 32.7 % completado'],['Listos para medir','2 · SON05, SON24'],['HP por habilitar','21,925 · de 26,031 HP'],['Fecha de actualización','Actualizado al 10/11/2026']]))
A('<ul class="check"><li>8 tablas del Excel cargadas, más dCalendario</li><li>13 relaciones, todas uno a varios y de dirección única</li><li>Todas las medidas con su formato</li><li>Lienzo 16:9, fondo al 0 % de transparencia y tema importado</li><li>5 segmentaciones y el botón Limpiar filtros</li><li>7 tarjetas (fecha y 6 KPI)</li><li>8 gráficos en sus paneles</li><li>15 tarjetas en la franja de insights</li><li>Los valores de la tabla de arriba coinciden</li></ul>')

A('<h2>Publicar y actualizar</h2><ol><li><b>Inicio › Publicar</b>, elige el área de trabajo <b>Desarrollo inicial</b> y acepta <b>Reemplazar</b>: mantiene el mismo enlace y los mismos permisos.</li><li>Para que se actualice solo en la nube, el Excel debe estar en el SharePoint/OneDrive de TEK-COM y Power BI debe leerlo desde ahí (no desde tu PC). Ya en la nube, programa el refresco en <b>Configuración del modelo semántico › Actualización programada</b>.</li><li>Cada día (o cuando quieras) descarga <b>Datos para Power BI</b> desde el sistema y reemplaza el archivo de SharePoint/OneDrive con el mismo nombre; el siguiente refresco trae los números nuevos.</li><li>Si el reporte ya está armado y todo quedó “para abajo”: cambia el lienzo a <b>Tipo: 16:9</b>, pon el fondo y mueve cada visual a las posiciones de estas tablas. No hace falta rehacerlos.</li></ol>')
A('<h2>Problemas comunes</h2>')
A(table(['Síntoma','Causa y arreglo'],[
 ['La tarjeta dice “104 K”','Valor de llamada › Unidades de visualización: Ninguno'],['Los meses salen en orden alfabético','MesCorto sin Ordenar por columna › MesNum'],
 ['Un filtro no mueve un gráfico','Falta la relación de esa dimensión con la tabla del gráfico (revisa las 13 relaciones)'],['Año o Mes no mueven las tarjetas ni los gráficos de nodos','Es normal: solo la producción (fInstalado) tiene fecha; lo demás es el estado actual'],
 ['El avance acumulado sale en blanco','dCalendario no está marcada como tabla de fechas, o falta la relación con fInstalado[Fecha]'],['El fondo se ve lavado o cortado','Transparencia en 0 % y ajuste Ajustar; lienzo en 16:9'],
 ['Error de sintaxis al pegar DAX','Archivo › Opciones › Configuración regional › Separadores DAX: estándar (con comas)'],['Aparecen íconos al pasar el mouse sobre los visuales','Es normal en edición; en el reporte publicado quedan ocultos'],['Un visual tapa a otro al hacer clic','Ver › Selección y ordena las capas; ponle nombre a cada visual']]))

css='''
@page { size: A4; margin: 16mm 15mm; }
body { font-family: Inter, sans-serif; color:#1F2026; font-size: 10.5pt; line-height: 1.45; }
h1 { font-size: 24pt; line-height:1.1; margin: 0 0 6px; letter-spacing:-.01em; }
h2 { font-size: 15pt; margin: 26px 0 8px; border-bottom: 2px solid #E3262F; padding-bottom: 3px; break-after: avoid; }
h3 { font-size: 11.5pt; margin: 14px 0 4px; break-after: avoid; }
.meta { color:#6B6E76; font-size: 9pt; margin: 0 0 8px; }
table { border-collapse: collapse; width: 100%; margin: 8px 0 12px; font-size: 9pt; break-inside: avoid; }
th { background: #F2F3F5; text-align:left; padding: 5px 7px; border: 1px solid #E1E2E6; }
td { padding: 5px 7px; border: 1px solid #E1E2E6; vertical-align: top; }
pre { background:#F5F5F6; border-radius:6px; padding: 9px 11px; font-family: "DejaVu Sans Mono", monospace; font-size: 7.6pt; line-height: 1.4; white-space: pre-wrap; word-break: break-word; margin: 6px 0 12px; break-inside: avoid; }
img.bg { width: 100%; border: 1px solid #E1E2E6; border-radius: 6px; margin: 6px 0; }
ul.check { list-style: none; padding-left: 0; } ul.check li:before { content: "☐  "; }
li { margin: 3px 0; }
'''
doc=f'<!doctype html><html lang="es"><head><meta charset="utf-8"><style>{css}</style></head><body>{"".join(body)}</body></html>'
(D/'GUIA_POWER_BI_FTTH.html').write_text(doc,encoding='utf-8')
print('html', len(doc))
