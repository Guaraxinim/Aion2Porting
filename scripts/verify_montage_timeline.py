import json
import math
from pathlib import Path

base = Path(__file__).resolve().parents[1] / 'reports' / 'montage-test'
read = lambda file: json.loads(file.read_text(encoding='utf-8-sig'))
montage = read(base / 'clip.json')
max_difference = 0.0
checked = 0
for name, start, stop in [('Start', 0, 30), ('Loop', 30, 45), ('End', 45, len(montage['Frames']))]:
    original = read(base / f'{name}-clip.json')
    assert [b['Name'] for b in original['Bones']] == [b['Name'] for b in montage['Bones']]
    for offset in range(min(stop - start, len(original['Frames']))):
        actual, expected = montage['Frames'][start + offset], original['Frames'][offset]
        difference = max(abs(a - e) for av, ev in zip(actual, expected) for a, e in zip(av, ev))
        assert math.isfinite(difference) and difference <= 0.00002, (name, offset, difference)
        max_difference = max(max_difference, difference)
        checked += 1
assert checked >= 75
result = dict(Passed=True, ComparedFrames=checked, Segments=3, MaxComponentDifference=max_difference)
(base / 'timeline-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
