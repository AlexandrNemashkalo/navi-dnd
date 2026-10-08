"""Локальный JSON-lines адаптер Silero. stdout содержит только ответы протокола."""
import json
import sys
import wave
from pathlib import Path

import torch

torch.set_num_threads(4)
model = torch.package.PackageImporter(str(Path(sys.argv[1]))).load_pickle("tts_models", "model")
model.to(torch.device("cpu"))
print(json.dumps({"ready": True}), flush=True)

for line in sys.stdin:
    try:
        request = json.loads(line)
        with torch.inference_mode():
            # Частота — из запроса: английская v3 на 48 кГц синтезирует вдвое дольше, ей хватает 24 кГц.
            rate = int(request.get("sample_rate", 48000))
            kwargs = {"speaker": request["voice"], "sample_rate": rate}
            if request.get("ssml_text"):
                try:
                    audio = model.apply_tts(ssml_text=request["ssml_text"], **kwargs)
                except Exception:
                    audio = model.apply_tts(text=request["text"], **kwargs)
            else:
                audio = model.apply_tts(text=request["text"], **kwargs)
        pcm = (audio.clamp(-1, 1) * 32767).to(torch.int16).numpy().tobytes()
        with wave.open(request["output"], "wb") as target:
            target.setnchannels(1)
            target.setsampwidth(2)
            target.setframerate(rate)
            target.writeframes(pcm)
        print(json.dumps({"ok": True}), flush=True)
    except Exception as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False), flush=True)
