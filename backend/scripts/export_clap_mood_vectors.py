#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Bulat Ruslanovich
"""Считает векторы настроений текстовой башней CLAP.

Текстовая и аудио-башни CLAP проецируют в одно пространство, поэтому близость эмбеддинга трека
к вектору описания «energetic workout music» говорит, насколько трек так звучит. Набор настроений
фиксированный, так что векторы считаются один раз здесь и лежат в репозитории: бэкенду не нужны ни
текстовая модель, ни токенизатор.

Каждое настроение — среднее нескольких описаний (prompt ensembling): одно описание шумит, среднее
устойчивее. Описания — про звучание, а не про ситуацию: CLAP не знает, что такое «дорога», но знает,
как звучит «driving rock with a steady beat».
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import torch
from transformers import ClapModel, ClapProcessor

DEFAULT_MODEL = "laion/larger_clap_music_and_speech"

MOODS: dict[str, list[str]] = {
    "workout": [
        "energetic high tempo workout music with a powerful driving beat",
        "intense pumping gym music with heavy bass",
        "fast motivating music with a strong rhythm",
    ],
    "drive": [
        "upbeat driving rock music with a steady beat",
        "road trip music with groovy bass and guitars",
        "cruising music with a confident mid tempo groove",
    ],
    "party": [
        "dance party music with a catchy upbeat beat",
        "club dance music with a four on the floor kick",
        "fun danceable pop song",
    ],
    "focus": [
        "calm instrumental music without vocals",
        "ambient background music for studying",
        "minimal lo-fi instrumental beats",
    ],
    "chill": [
        "relaxed laid-back chill music",
        "mellow chillout music with soft beats",
        "smooth easy listening music",
    ],
    "sleep": [
        "very quiet slow ambient music",
        "soft gentle peaceful piano music",
        "slow dreamy calm soundscape",
    ],
    "happy": [
        "happy cheerful upbeat song",
        "joyful bright music in a major key",
        "feel good positive music",
    ],
    "sad": [
        "sad melancholic song",
        "slow emotional ballad",
        "heartbreaking melancholy music in a minor key",
    ],
}


def normalize(vector: np.ndarray) -> np.ndarray:
    norm = np.linalg.norm(vector)
    return vector / norm if norm > 1e-12 else vector


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    print(f"loading {args.model}")
    model = ClapModel.from_pretrained(args.model).eval()
    processor = ClapProcessor.from_pretrained(args.model)

    moods = {}
    for key, prompts in MOODS.items():
        inputs = processor(text=prompts, return_tensors="pt", padding=True)
        with torch.no_grad():
            vectors = model.get_text_features(**inputs).numpy()

        vectors = np.stack([normalize(v) for v in vectors])
        moods[key] = {
            "prompts": prompts,
            "vector": [round(float(x), 7) for x in normalize(vectors.mean(axis=0))],
        }
        print(f"  {key}: {len(prompts)} prompts")

    keys = list(moods)
    matrix = np.stack([np.asarray(moods[k]["vector"]) for k in keys])
    print("\ncosine between moods:")
    print("         " + " ".join(f"{k[:7]:>7}" for k in keys))
    for k, row in zip(keys, matrix @ matrix.T):
        print(f"{k[:8]:>8} " + " ".join(f"{v:7.3f}" for v in row))

    payload = {"modelId": args.model, "dimension": int(matrix.shape[1]), "moods": moods}
    args.out.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"\nwrote {args.out}")


if __name__ == "__main__":
    main()
