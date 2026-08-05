import pytest

from app.main import app, get_recognizer
from app.recognizer import PlateRecognitionResult
from fastapi.testclient import TestClient


class FakeRecognizer:
    def __init__(self, result: PlateRecognitionResult) -> None:
        self._result = result

    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult:
        return self._result


@pytest.fixture
def client_with():
    """Fixture that provides a client factory with guaranteed dependency cleanup."""

    def _make(result: PlateRecognitionResult) -> TestClient:
        app.dependency_overrides[get_recognizer] = lambda: FakeRecognizer(result)
        return TestClient(app)

    yield _make
    app.dependency_overrides.clear()


def test_recognize_returns_plate_and_confidence(client_with):
    client = client_with(PlateRecognitionResult(plate="ABC1D23", confidence=0.94))

    response = client.post(
        "/recognize",
        files={"file": ("snapshot.jpg", b"\xff\xd8\xff\xdb fake-jpeg-bytes", "image/jpeg")},
    )

    assert response.status_code == 200
    assert response.json() == {"plate": "ABC1D23", "confidence": 0.94}


def test_recognize_reports_no_read_when_nothing_found(client_with):
    client = client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.post(
        "/recognize",
        files={"file": ("snapshot.jpg", b"\xff\xd8\xff\xdb fake-jpeg-bytes", "image/jpeg")},
    )

    assert response.status_code == 200
    assert response.json() == {"plate": None, "confidence": 0.0}


def test_recognize_rejects_non_image_upload(client_with):
    client = client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.post(
        "/recognize",
        files={"file": ("notes.txt", b"hello", "text/plain")},
    )

    assert response.status_code == 400


def test_health_returns_ok(client_with):
    client = client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
