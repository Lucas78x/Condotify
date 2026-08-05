from __future__ import annotations

from fastapi import Depends, FastAPI, HTTPException, UploadFile

from .recognizer import FastAlprRecognizer, PlateRecognitionResult, PlateRecognizer

app = FastAPI(title="Condotify LPR OCR")

_default_recognizer = FastAlprRecognizer()


def get_recognizer() -> PlateRecognizer:
    return _default_recognizer


@app.post("/recognize")
async def recognize(file: UploadFile, recognizer: PlateRecognizer = Depends(get_recognizer)) -> dict:
    content_type = file.content_type or ""
    if not content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="O arquivo enviado precisa ser uma imagem.")

    image_bytes = await file.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="Imagem vazia.")

    result: PlateRecognitionResult = recognizer.recognize(image_bytes)
    return {"plate": result.plate, "confidence": result.confidence}


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}
