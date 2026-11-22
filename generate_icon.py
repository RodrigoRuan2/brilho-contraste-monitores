"""Generate the app icon with a transparent background (requires Pillow)."""

from pathlib import Path

from PIL import Image, ImageDraw


SCALE = 4
image = Image.new("RGBA", (64 * SCALE, 64 * SCALE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)


def box(points):
    return tuple(value * SCALE for value in points)


frame = (168, 185, 210, 255)
cyan = (95, 211, 192, 255)
draw.rounded_rectangle(box((5, 8, 59, 48)), radius=6 * SCALE, outline=frame, width=4 * SCALE)
draw.ellipse(box((24, 17, 40, 33)), fill=cyan)
for x1, y1, x2, y2 in [
    (32, 11, 32, 15), (32, 35, 32, 39), (18, 25, 22, 25), (42, 25, 46, 25),
    (22, 15, 25, 18), (39, 32, 42, 35), (22, 35, 25, 32), (39, 18, 42, 15),
]:
    draw.line(box((x1, y1, x2, y2)), fill=cyan, width=3 * SCALE)
draw.rectangle(box((28, 49, 36, 55)), fill=frame)
draw.rounded_rectangle(box((19, 55, 45, 59)), radius=2 * SCALE, fill=frame)

image.save(
    Path(__file__).with_name("BrilhoDosMonitores.ico"),
    format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
