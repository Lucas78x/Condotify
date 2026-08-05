from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class PlateRecognitionResult:
    plate: str | None
    confidence: float


class PlateRecognizer(Protocol):
    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult: ...


class FastAlprRecognizer:
    """Self-hosted recognizer backed by fast-alpr (YOLO plate detector + OCR).

    The model is loaded lazily so importing this module never requires the
    (large) model weights to be present - keeps unit tests fast and offline.
    Model choice/tuning against real camera footage is a calibration task,
    not an architecture decision - swap the model names below as needed.
    """

    def __init__(
        self,
        detector_model: str = "yolo-v9-t-640-license-plate-end2end",
        ocr_model: str = "cct-xs-v1-global-model",
    ) -> None:
        self._detector_model = detector_model
        self._ocr_model = ocr_model
        self._alpr = None

    def _ensure_loaded(self):
        if self._alpr is None:
            from fast_alpr import ALPR

            self._alpr = ALPR(detector_model=self._detector_model, ocr_model=self._ocr_model)
        return self._alpr

    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult:
        import cv2
        import numpy as np

        alpr = self._ensure_loaded()
        array = np.frombuffer(image_bytes, dtype=np.uint8)
        image = cv2.imdecode(array, cv2.IMREAD_COLOR)
        if image is None:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        results = alpr.predict(image)
        if not results:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        best = max(results, key=lambda r: r.ocr.text_confidence if r.ocr else 0.0)
        if best.ocr is None:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        return PlateRecognitionResult(plate=best.ocr.text, confidence=float(best.ocr.text_confidence))
