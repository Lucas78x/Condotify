FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

# Warm the model at build time instead of on the first /recognize call.
# FastAlprRecognizer loads lazily (see app/recognizer.py) so unit tests stay
# fast/offline, but that means an uninstrumented container pays the full
# download+load cost on its first real request - which exceeds the .NET
# client's 10s HTTP timeout (CondotifyAPI/Program.cs's
# AddHttpClient<ILprRecognitionClient, ...>) and would silently fail the
# first several gate events after every deploy/restart. Baking the weights
# into this image layer also means a fully air-gapped deployment can still
# start up and serve /recognize without needing network access at runtime.
RUN python -c "from app.recognizer import FastAlprRecognizer; FastAlprRecognizer()._ensure_loaded()"

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
