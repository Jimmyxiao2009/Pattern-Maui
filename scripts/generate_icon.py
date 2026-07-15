from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFilter

root = Path(__file__).resolve().parents[1]
target = "mobile" if "--mobile" in sys.argv else "desktop"
icons = root / "apps" / target / "src-tauri" / "icons"
icons.mkdir(parents=True, exist_ok=True)

scale = 4
size = 256
canvas = Image.new("RGBA", (size * scale, size * scale), (17, 16, 19, 255))
draw = ImageDraw.Draw(canvas)

# Soft amber halo and cat-eye iris, matching the Catseye design tokens.
halo = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
halo_draw = ImageDraw.Draw(halo)
halo_draw.ellipse((50 * scale, 50 * scale, 206 * scale, 206 * scale), fill=(232, 163, 61, 110))
halo = halo.filter(ImageFilter.GaussianBlur(20 * scale))
canvas.alpha_composite(halo)

draw = ImageDraw.Draw(canvas)
draw.ellipse((58 * scale, 58 * scale, 198 * scale, 198 * scale), fill=(184, 125, 34, 255))
draw.ellipse((66 * scale, 64 * scale, 190 * scale, 190 * scale), fill=(232, 163, 61, 255))
draw.ellipse((82 * scale, 76 * scale, 145 * scale, 137 * scale), fill=(255, 222, 158, 190))
draw.rounded_rectangle((116 * scale, 78 * scale, 140 * scale, 180 * scale), radius=12 * scale, fill=(21, 18, 23, 255))

image = canvas.resize((size, size), Image.Resampling.LANCZOS)
image.save(icons / "icon.png")
image.save(icons / "icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
