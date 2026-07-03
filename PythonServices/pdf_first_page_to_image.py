import sys
import fitz

pdf_path = sys.argv[1]
output_path = sys.argv[2]

doc = fitz.open(pdf_path)
page = doc.load_page(0)
pix = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
pix.save(output_path)
doc.close()