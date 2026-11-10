"""Genera el fondo del lienzo de Power BI (1280 x 720, exportado a 2560 x 1440) del tablero de despliegue FTTH.

Uso:  python3 generar_fondo.py            (crea fondo_dashboard_despliegue_ftth.png junto a este archivo)
Las posiciones son las de la guía: barra lateral, encabezado, 6 tarjetas, 8 paneles y la franja de 5 insights.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
FONTS = Path("/usr/share/fonts/opentype/inter")
LOGO = HERE.parent.parent / "src" / "Tekcom.Gestion.Web" / "wwwroot" / "branding" / "logo.png"
OUT = HERE / "fondo_dashboard_despliegue_ftth.png"

S = 4                      # sobremuestreo para bordes suaves
W, H = 1280, 720
RED, INK, GRAY_BG, PANEL, MID, LABEL = "#E3262F", "#1F2026", "#F3F4F6", "#FFFFFF", "#E3E4E8", "#9A9CA3"

img = Image.new("RGB", (W * S, H * S), GRAY_BG)
d = ImageDraw.Draw(img)


def font(name, size):
    return ImageFont.truetype(str(FONTS / f"Inter-{name}.otf"), int(size * S))


def box(x, y, w, h, fill, r=8, outline=None):
    d.rounded_rectangle([x * S, y * S, (x + w) * S, (y + h) * S], radius=r * S, fill=fill, outline=outline, width=S if outline else 0)


def text(x, y, s, f, fill, spacing=0.0, anchor="la"):
    if spacing == 0:
        d.text((x * S, y * S), s, font=f, fill=fill, anchor=anchor)
        return
    cx = x * S
    for ch in s:
        d.text((cx, y * S), ch, font=f, fill=fill, anchor="la")
        cx += d.textlength(ch, font=f) + spacing * S


def line(points, fill, width=1.2):
    d.line([(px * S, py * S) for px, py in points], fill=fill, width=int(width * S), joint="curve")


# ---------------------------------------------------------------- barra lateral
d.rectangle([0, 0, 200 * S, H * S], fill=INK)
box(16, 16, 168, 44, PANEL, 6)
if LOGO.exists():
    logo = Image.open(LOGO).convert("RGBA")
    bbox = logo.getbbox()
    if bbox:
        logo = logo.crop(bbox)
    target_h = 34 * S
    ratio = target_h / logo.height
    logo = logo.resize((int(logo.width * ratio), int(target_h)), Image.LANCZOS)
    img.paste(logo, (int((16 + 84) * S - logo.width / 2), int(38 * S - logo.height / 2)), logo)
text(16, 86, "FILTROS", font("ExtraBold", 9), "#FFFFFF", 1.4)
box(16, 101, 16, 2, RED, 1)
for i, label in enumerate(["AÑO", "MES", "BRIGADA", "SUPERVISOR", "ESTADO DEL NODO"]):
    y = 134 + 64 * i
    text(16, y - 14, label, font("Bold", 6.4), LABEL, 1.0)
    box(16, y, 168, 32, "#FFFFFF", 5)
for i, word in enumerate(["CONSTRUIR", "MEDIR", "ENTREGAR"]):
    text(16, 540 + 14 * i, word, font("Bold", 6.6), "#E6E7EA", 1.8)
box(16, 584, 16, 2, RED, 1)
d.line([(16 * S, 636 * S), (184 * S, 636 * S)], fill="#3A3C45", width=S)
box(52, 652, 132, 36, RED, 6)
# borrador (goma)
d.polygon([(21 * S, 676 * S), (29 * S, 662 * S), (41 * S, 666 * S), (33 * S, 680 * S)], outline="#FFFFFF", width=int(1.2 * S))
line([(25, 672), (36, 676)], "#FFFFFF", 1)

# ---------------------------------------------------------------- encabezado
text(216, 14, "DASHBOARD DE CONTROL DEL DESPLIEGUE FTTH", font("ExtraBold", 16.5), INK)
box(216, 38, 22, 2, RED, 1)
text(216, 43, "Seguimiento del avance, la producción y la entrega", font("Regular", 6.6), "#6B6E76")
box(1116, 18, 144, 32, PANEL, 7, outline="#E4E5E8")
box(1126, 28, 12, 11, None, 2, outline=RED)
d.line([(1126 * S, 32 * S), (1138 * S, 32 * S)], fill=RED, width=int(1.1 * S))

# ---------------------------------------------------------------- tarjetas KPI
def icon(kind, cx, cy, color):
    w = 1.3
    if kind == 0:      # tendencia
        line([(cx - 6, cy + 4), (cx - 2, cy), (cx + 1, cy + 2), (cx + 6, cy - 4)], color, w)
        line([(cx + 3, cy - 4), (cx + 6, cy - 4), (cx + 6, cy - 1)], color, w)
    elif kind == 1:    # cable
        d.arc([(cx - 7) * S, (cy - 5) * S, (cx + 7) * S, (cy + 7) * S], 200, 340, fill=color, width=int(w * S))
        line([(cx - 5, cy - 5), (cx - 5, cy + 1)], color, 2.2)
        line([(cx + 5, cy - 5), (cx + 5, cy + 1)], color, 2.2)
    elif kind == 2:    # tarjeta FAT
        box(cx - 5, cy - 5, 10, 10, None, 1.5, outline=color)
        for k in (-3, 0, 3):
            line([(cx - 7, cy + k), (cx - 5, cy + k)], color, 1)
            line([(cx + 5, cy + k), (cx + 7, cy + k)], color, 1)
    elif kind == 3:    # caja
        box(cx - 6, cy - 5, 12, 10, None, 1.5, outline=color)
        line([(cx - 6, cy - 1), (cx + 6, cy - 1)], color, 1)
    elif kind == 4:    # casa
        d.polygon([((cx - 6) * S, (cy) * S), (cx * S, (cy - 6) * S), ((cx + 6) * S, cy * S), ((cx + 4) * S, cy * S), ((cx + 4) * S, (cy + 5) * S), ((cx - 4) * S, (cy + 5) * S), ((cx - 4) * S, cy * S)], outline=color, width=int(w * S))
    else:              # nodo / red
        d.ellipse([(cx - 2) * S, (cy - 2) * S, (cx + 2) * S, (cy + 2) * S], outline=color, width=int(w * S))
        for dx, dy in ((-6, -4), (6, -4), (-6, 4), (6, 4)):
            line([(cx, cy), (cx + dx * 0.8, cy + dy * 0.8)], color, 0.9)
            d.ellipse([(cx + dx - 1.2) * S, (cy + dy - 1.2) * S, (cx + dx + 1.2) * S, (cy + dy + 1.2) * S], fill=color)


for i in range(6):
    x = 216 + 176 * i
    dark = i < 2
    box(x, 66, 168, 84, INK if dark else MID, 8)
    icon(i, x + 84, 80, "#FFFFFF" if dark else "#4A4C55")
    if dark:
        box(x + 78, 143, 12, 2, RED, 1)

# ---------------------------------------------------------------- paneles de gráficos
xs = [(216, 312), (538, 248), (796, 229), (1035, 229)]
for y, h in ((160, 214), (384, 196)):
    for x, w in xs:
        box(x, y, w, h, PANEL, 8, outline="#E9EAED")

# ---------------------------------------------------------------- franja de insights
box(216, 588, 1048, 124, MID, 8)
text(230, 596, "INSIGHTS CLAVES DEL DESPLIEGUE", font("ExtraBold", 6.8), INK, 0.8)
d.ellipse([(218 + 0) * S, 0, 0, 0], fill=None) if False else None
titles = ["Nodo más avanzado en obra", "Brigada con mayor avance", "FATs por instalar", "Listos para medir", "HP por habilitar"]
for i, title in enumerate(titles):
    x = 224 + 208 * i
    box(x, 612, 200, 96, PANEL, 6)
    d.rectangle([x * S, 614 * S, (x + 3) * S, 706 * S], fill=RED)
    d.ellipse([(x + 12) * S, 622 * S, (x + 19) * S, 629 * S], outline=RED, width=int(1.1 * S))
    text(x + 24, 621, title, font("SemiBold", 6.6), INK)

out = img.resize((W * 2, H * 2), Image.LANCZOS)
out.save(OUT, optimize=True)
print("creado", OUT, out.size)
