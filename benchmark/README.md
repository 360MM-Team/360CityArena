# Benchmark Data

This directory contains 360CityArena task reference assets and release manifests.

- `assets/localization/`: localization reference map.
- `assets/landmark_images/`: landmark-search image references.
- `assets/navigation_maps/`: map-navigation references.
- `manifests/`: the pinned Hugging Face Dataset source and asset inventory.

Task definitions are maintained only in
[`hal-utokyo/360CityArena`](https://huggingface.co/datasets/hal-utokyo/360CityArena).
The runner loads the exact revision recorded in `manifests/task_manifest.json`
and fails closed on invalid rows, duplicate IDs, or an unexpected inventory.

Maintainers can validate and release the Hugging Face Dataset by
following `docs/huggingface_release.md`.
