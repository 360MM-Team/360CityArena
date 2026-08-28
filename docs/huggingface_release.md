# Hugging Face Dataset Release

`hal-utokyo/360CityArena` is the canonical source for all 175 task definitions.
The GitHub repository stores only a pinned Hub reference in
`benchmark/manifests/task_manifest.json`; task CSV files are not maintained in
two locations.

## Install and authenticate

Use an isolated Python environment when possible:

```bash
python -m pip install --upgrade datasets huggingface-hub Pillow
hf auth login
```

Never place a Hugging Face token in the repository or in a command-line
argument. The publishing client uses the token stored by `hf auth login`.

## Validate the pinned release

From the repository root:

```bash
uv run --project python python -c \
  "from cityarena.tasks.catalog import TASKS; assert len(TASKS) == 175"
```

The expected release inventory is:

- 175 task rows
- seven task families with 25 tasks each
- 75 rows that use a reference image
- 51 unique reference images

The localization map is shared by 25 rows, which explains the difference
between rows with images and unique image files.

The official repository can remain private during validation. Confirm loading
while authenticated:

```python
from datasets import load_dataset

dataset = load_dataset("hal-utokyo/360CityArena")
assert dataset["test"].num_rows == 175
```

When task data changes, update the Hub dataset first, validate the new Hub commit,
then replace only the `revision` value in `task_manifest.json`. Experiments must
not use a moving `main` revision.

## Public release

After private validation passes, change the existing repository visibility to
public in its Hugging Face Settings page. Do not create a second dataset copy.

After publication:

1. Confirm that the repository is public and Data Studio shows 175 rows.
2. Run `load_dataset("hal-utokyo/360CityArena")` in a clean environment.
3. Check that the image column renders for task IDs 1001, 3001, and 5001.
4. Add the dataset URL to the GitHub and project pages.
5. Confirm that the dataset appears on the Hugging Face paper page for
   [arXiv:2608.08814](https://huggingface.co/papers/2608.08814).

Dataset records and benchmark assets use CC BY-NC 4.0 unless third-party terms
apply. Map-derived assets must retain the OpenStreetMap contributor attribution
and ODbL notice described in `DATA_LICENSE` and `NOTICE`.
