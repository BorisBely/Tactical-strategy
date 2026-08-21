import math
from pathlib import Path

def clamp01(x):
    return max(0.0, min(1.0, x))

def smooth(t):
    return t * t * (3 - 2 * t)

def distance_factor(d, near=20, far=500, far_f=0.08):
    if d <= near:
        return 1.0
    if d >= far:
        return far_f
    t = (d - near) / (far - near)
    return 1.0 + (far_f - 1.0) * smooth(t)

def fov_factor(ang, half=45, edge=0.15):
    t = clamp01(abs(ang) / half)
    return 1.0 + (edge - 1.0) * smooth(t)

def movement_factor(speed, walk=0.6, run=3.2, wm=1.15, rm=1.35, cap=1.5):
    m = 1.0
    if speed >= run:
        m = rm
    elif speed >= walk:
        m = wm
    return min(cap, m)

def visibility(d, f, e, m):
    return clamp01(d * f * e * m)

def integrate(p, q, dt, acq=0.35, loss=2.5, thr=0.02):
    ar = 1 / max(0.05, acq)
    lr = 1 / max(0.1, loss)
    if q >= thr:
        return clamp01(p + q * ar * dt)
    return clamp01(p - (1 - q) * lr * dt)

lines = []
pass_n = 0
fail_n = 0

def check(name, ok, detail):
    global pass_n, fail_n
    if ok:
        pass_n += 1
        lines.append(f"PASS {name} | {detail}")
    else:
        fail_n += 1
        lines.append(f"FAIL {name} | {detail}")

d10 = distance_factor(10)
d100 = distance_factor(100)
d400 = distance_factor(400)
check("Math_DistanceMonotone", d10 >= d100 >= d400, f"d10={d10:.3f} d100={d100:.3f} d400={d400:.3f}")
f0 = fov_factor(0)
f50 = fov_factor(50)
check("Math_FovMonotone", f0 >= f50, f"f0={f0:.3f} f50={f50:.3f}")
qFull = visibility(d100, f0, 1, 1)
qLow = visibility(d100, f0, 0.1, 1)
check("Math_ExposureMonotone", qFull >= qLow, f"qFull={qFull:.3f} qLow={qLow:.3f}")
qIdle = visibility(d400, f50, 0.1, movement_factor(0))
qRun = visibility(d400, f50, 0.1, movement_factor(4.5))
check("Math_MovementHelpsButNotMagic", qRun > qIdle and qRun < 0.5, f"idle={qIdle:.3f} run={qRun:.3f}")
acq = integrate(0, 1, 0.1)
lost = integrate(1, 0, 0.1)
check("Math_AcquireFasterThanLose", (acq - 0) > (1 - lost), f"acqDelta={acq:.3f} loseDelta={1-lost:.3f}")
p = 0.0
for _ in range(20):
    p = integrate(p, 1, 0.05)
soft = p
for _ in range(3):
    soft = integrate(soft, 0, 0.05)
check("Math_SoftLoseKeepsProgress", soft > 0.5, f"before={p:.3f} afterGap={soft:.3f}")
qA = visibility(distance_factor(10), fov_factor(0), 1, 1)
qF = visibility(distance_factor(400), fov_factor(50), 1, 1)
check("Math_PresetA_BetterThanF", qA > qF, f"A={qA:.3f} F={qF:.3f}")
result = "PASS" if fail_n == 0 else "FAIL"
lines.append("---")
lines.append(f"RESULT={result} pass={pass_n} fail={fail_n}")
out = Path(r"d:/Unity project/My project 001/Assets/_Docs/Logs/Tests")
out.mkdir(parents=True, exist_ok=True)
text = "\n".join(lines) + "\n"
(out / "DetectionG1_Math_AGENT.txt").write_text(text, encoding="utf-8")
(out / "DetectionG1_Math_LAST.txt").write_text(text, encoding="utf-8")
print(text)
