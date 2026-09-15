
# ============================================================
# DLG QA Plan Generator — Animated sweep
# Output: docs/img/dlg-sweep.gif
# ============================================================

import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle
import matplotlib.animation as animation
from matplotlib.animation import PillowWriter
import numpy as np

# ---------- Config ----------
X1, X2 = -50.0, 50.0
Y1, Y2 = -90.0, 90.0
SWEEP_START, SWEEP_END = -70.0, 70.0
N_STEPS = 11                    # control points
GAP = 10.0                      # mm (demo gap)

FPS         = 20
SECONDS_PER_CYCLE = 4.0
N_FRAMES    = int(FPS * SECONDS_PER_CYCLE)
HOLD_FRAMES = int(FPS * 0.6)    # pause at extremes

# ---------- Palette ----------
NAVY        = '#1C3049'
ACCENT      = '#0078A8'
ACCENT_DARK = '#005A80'
ACCENT_SOFT = '#9CC7DA'
TX_COLOR    = '#C0392B'
JAW_FILL    = '#EDF2F8'
JAW_EDGE    = '#2A3F5F'
GRID        = '#DCE3EB'
MUTED       = '#6B7A88'

# ---------- Figure ----------
fig, ax = plt.subplots(figsize=(9, 8.5))
fig.patch.set_facecolor('white')

ax.set_xlim(-95, 95)
ax.set_ylim(-115, 115)
ax.set_aspect('equal')
ax.set_xticks([-70, -50, 0, 50, 70])
ax.set_yticks([-90, 0, 90])
ax.tick_params(labelsize=8, colors=MUTED)
ax.grid(True, linestyle=':', color=GRID, linewidth=0.5, zorder=0)
for spine in ax.spines.values():
    spine.set_edgecolor('#E0E6EC')
    spine.set_linewidth(0.7)
ax.set_xlabel('X (mm)', fontsize=8, color=MUTED)
ax.set_ylabel('Y (mm)', fontsize=8, color=MUTED)

# Title
title = ax.set_title('DLG Sweeping Gap  ·  control point 0 / 10',
                     fontsize=12, fontweight='bold', color=NAVY, pad=12)
subtitle = fig.text(0.5, 0.925,
                    f'Gap = {int(GAP)} mm   |   Sweep −70 → +70 mm   |   11 CP',
                    ha='center', fontsize=9, color=MUTED, style='italic')

# ---------- Jaws ----------
ax.add_patch(Rectangle((X1, Y1), X2 - X1, Y2 - Y1,
                       facecolor=JAW_FILL, edgecolor='none', zorder=1))
jaw_rect = Rectangle((X1, Y1), X2 - X1, Y2 - Y1,
                     facecolor='none', edgecolor=JAW_EDGE,
                     linewidth=2.0, zorder=3)
ax.add_patch(jaw_rect)

# ---------- Sweep trail (previous positions) ----------
trail_lines = []
for _ in range(6):
    l0 = ax.plot([], [], color=ACCENT, linewidth=2.2,
                 alpha=0.0, solid_capstyle='butt', zorder=4)[0]
    l1 = ax.plot([], [], color=ACCENT, linewidth=2.2,
                 alpha=0.0, solid_capstyle='butt', zorder=4)[0]
    trail_lines.append((l0, l1))

# ---------- Active banks ----------
bank0_line = ax.plot([], [], color=ACCENT, linewidth=5,
                     solid_capstyle='butt', zorder=6)[0]
bank1_line = ax.plot([], [], color=ACCENT, linewidth=5,
                     solid_capstyle='butt', zorder=6)[0]

# Aperture fill (highlights the gap)
aperture = Rectangle((0, Y1), GAP, Y2 - Y1,
                     facecolor=ACCENT, alpha=0.12,
                     edgecolor='none', zorder=5)
ax.add_patch(aperture)

# ---------- Bank labels ----------
label0 = ax.text(0, Y2 + 6, 'Bank 0', ha='center',
                 fontsize=8.5, color=ACCENT, fontweight='bold')
label1 = ax.text(0, Y2 + 6, 'Bank 1', ha='center',
                 fontsize=8.5, color=ACCENT, fontweight='bold')

# ---------- Sweep range indicator (bottom) ----------
ax.annotate('', xy=(SWEEP_END, -100), xytext=(SWEEP_START, -100),
            arrowprops=dict(arrowstyle='->', color=NAVY, lw=1.8))
ax.annotate('', xy=(SWEEP_START, -100), xytext=(SWEEP_END, -100),
            arrowprops=dict(arrowstyle='->', color=NAVY, lw=1.8))
ax.text(0, -112, 'Sweep range',
        ha='center', fontsize=8, color=NAVY, fontweight='bold')

# Position indicator dot on the sweep range
pos_dot = ax.plot([], [], 'o', color=ACCENT_DARK,
                  markersize=9, zorder=7,
                  markeredgecolor='white', markeredgewidth=1.2)[0]

# Current position label
pos_label = ax.text(0, 0, '', ha='center', va='center',
                    fontsize=9, color=NAVY, fontweight='bold',
                    zorder=8,
                    bbox=dict(boxstyle='round,pad=0.35',
                              facecolor='white', edgecolor=NAVY,
                              linewidth=0.8, alpha=0.95))

# ---------- Precompute positions (ping-pong) ----------
raw = np.linspace(SWEEP_START, SWEEP_END, N_STEPS)
# Build full sweep with hold at ends, back and forth
def build_timeline():
    fwd = np.linspace(SWEEP_START, SWEEP_END, N_FRAMES // 2)
    bwd = np.linspace(SWEEP_END, SWEEP_START, N_FRAMES - N_FRAMES // 2)
    return np.concatenate([fwd, bwd])

positions = build_timeline()

# ---------- Update ----------
def update(frame):
    center = positions[frame]
    b0 = center - GAP / 2
    b1 = center + GAP / 2

    # Active banks
    bank0_line.set_data([b0, b0], [Y1, Y2])
    bank1_line.set_data([b1, b1], [Y1, Y2])

    # Aperture fill
    aperture.set_x(b0)
    aperture.set_width(GAP)

    # Labels above banks
    label0.set_position((b0, Y2 + 6))
    label1.set_position((b1, Y2 + 6))

    # Sweep trail (past positions)
    trail_span = 18  # frames back
    for k, (l0, l1) in enumerate(trail_lines):
        idx = frame - (k + 1) * 3
        if idx < 0:
            l0.set_alpha(0.0); l1.set_alpha(0.0)
            continue
        c = positions[idx]
        t0 = c - GAP / 2
        t1 = c + GAP / 2
        l0.set_data([t0, t0], [Y1, Y2]); l0.set_alpha(0.18 - k * 0.025)
        l1.set_data([t1, t1], [Y1, Y2]); l1.set_alpha(0.18 - k * 0.025)

    # Position dot on the sweep range
    pos_dot.set_data([center], [-100])

    # Position label (following the gap)
    pos_label.set_position((center, 0))
    pos_label.set_text(f'{center:+.1f} mm')

    # Compute nearest CP index
    cp_index = int(np.round((center - SWEEP_START) /
                            (SWEEP_END - SWEEP_START) * (N_STEPS - 1)))
    cp_index = max(0, min(N_STEPS - 1, cp_index))
    title.set_text(f'DLG Sweeping Gap  ·  control point {cp_index} / {N_STEPS - 1}')

    return (bank0_line, bank1_line, aperture, label0, label1,
            pos_dot, pos_label, title, *sum(trail_lines, ()))

# ---------- Animate ----------
anim = animation.FuncAnimation(
    fig, update, frames=N_FRAMES,
    interval=1000 / FPS, blit=False
)

writer = PillowWriter(fps=FPS)
anim.save('dlg-sweep.gif', writer=writer, dpi=100)
plt.close(fig)

print('✓ Saved: dlg-sweep.gif')
