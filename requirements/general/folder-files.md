# Mixed folder content

Status: Implemented for the formats listed below.

## Supported content

- Code and other UTF-8 text of any extension or no extension; BOM-marked UTF-16 and UTF-32.
- PDF text and supported embedded scan images, DOCX/DOCM, XLSX/XLSM, PPTX/PPTM, ODT/ODS/ODP.
- PNG, JPEG, WebP, BMP, GIF and ICO images, using the enabled selected local model when it advertises vision support. The reader extracts visible text and a concise description without executing instructions found in images.
- Legacy Office binary formats, audio/video, encrypted or damaged documents and unsupported PDF image filters remain unsupported. Report skipped-file reasons in the local file list rather than treating arbitrary bytes as text. Recognition does not promise perfect OCR.

## Data flow and bounds

1. Traverse the selected folder using existing secret-file, service-directory and link exclusions. Source files remain unchanged.
2. Read at most 128 KiB for plain text or 8 MiB for a supported document/image; bound total scan input to 64 MiB, included files to 160 and inspected entries to 15,000.
3. Parse documents locally. Never run macros, evaluate formulas, follow external links or extract executables. Bound PDFs to 100 pages, ZIP containers to 512 entries, XML to 4 MiB per selected part and 16 MiB combined, shared strings to 10,000 and extracted output to 96,000 characters per file.
4. Limit decoded images to 16 million pixels and at most 2048 pixels per side for the model. Strip metadata through PNG re-encoding. Scanned PDF extraction handles at most 8 images per page. Every image request revalidates the selected loopback model and its vision capability; no remote fallback is permitted. Image inference has a two-minute deadline and a 256 KiB response limit.
5. Anonymize extracted text, repeat anonymization after later entity discovery, trim and require review. Only anonymized text and ordinary file aliases enter remote requests; image bytes never enter assistant history or local chat storage. Unknown filename extensions use `.txt` aliases.
6. Keep skipped-file paths and reasons only in session RAM; retain at most 160 entries. Cancellation rejects late image and preparation results.

## Acceptance criteria

Use synthetic text, code, Office archives, PDFs and images in one folder. Verify extraction, aliases and anonymization; validate malformed XML and compressed limits; cancel delayed image reading; replace a local model with a remote or text-only model and verify that image bytes are never sent to it. Verify existing exclusions and session cleanup.

## References

- [Ollama vision requests](https://docs.ollama.com/capabilities/vision)
- [PdfPig text extraction](https://github.com/UglyToad/PdfPig)
