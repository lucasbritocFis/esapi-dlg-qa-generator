
# ============================================================
# DLG QA Plan Generator — Field Geometry (Static, v2)
# Output: docs/img/diagram.png (300 DPI)
# ============================================================

import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle, FancyArrowPatch
from matplotlib.lines import Line2D
import matplotlib as mpl

# ---------- Geometry (mm) ----------
X1, X2 = -50.0, 50.0
Y1, Y2 = -90.0, 90.0
OPEN_PAD = 10.0
TX_OVERREACH = 10.0
TX_WIDTH = 1.0
SWEEP_START, SWEEP_END = -70.0, 70.0
GAP_DEMO = 10.0

# ---------- Palette (matches app UI) ----------
NAVY        = '#1C3049'
ACCENT      = '#0078A8'
ACCENT_DARK = '#005A80'
TX_COLOR    = '#C0392B'
JAW_FILL    = '#EDF2F8'
JAW_EDGE    = '#2A3F5F'
GRID        = '#DCE3EB'
TEXT_DARK   = '#2C3E50'
MUTED       = '#6B7A88'

mpl.rcParams['font.family'] = 'DejaVu Sans'
mpl.rcParams['axes.linewidth'] = 0.8

# ---------- Helpers ----------
def draw_jaws(ax, show_dims=False):
    ax.add_patch(Rectangle((X1, Y1), X2 - X1, Y2 - Y1,
                           facecolor=JAW_FILL, edgecolor='none', zorder=1))
    ax.add_patch(Rectangle((X1, Y1), X2 - X1, Y2 - Y1,
                           facecolor='none', edgecolor=JAW_EDGE,
                           linewidth=2.0, zorder=3))
    if show_dims:
        # Horizontal dimension arrow
        ax.annotate('', xy=(X1, Y2 + 8), xytext=(X2, Y2 + 8),
                    arrowprops=dict(arrowstyle='<->', color=MUTED, lw=0.8))
        ax.text(0, Y2 + 12, '100 mm', ha='center', va='bottom',
                fontsize=7.5, color=MUTED)
        # Vertical dimension arrow
        ax.annotate('', xy=(X2 + 8, Y1), xytext=(X2 + 8, Y2),
                    arrowprops=dict(arrowstyle='<->', color=MUTED, lw=0.8))
        ax.text(X2 + 12, 0, '180 mm', ha='left', va='center',
                fontsize=7.5, color=MUTED, rotation=90)

def setup_panel(ax, title, show_dims=False):
    ax.set_xlim(-90, 90)
    ax.set_ylim(-115, 115)
    ax.set_aspect('equal')
    ax.set_title(title, fontsize=10.5, fontweight='bold',
                 color=NAVY, pad=10)
    ax.set_xticks([-70, -50, 0, 50, 70])
    ax.set_yticks([-90, 0, 90])
    ax.tick_params(labelsize=7.5, colors=TEXT_DARK)
    ax.grid(True, linestyle=':', color=GRID, linewidth=0.5, zorder=0)
    for spine in ax.spines.values():
        spine.set_edgecolor('#E0E6EC')
        spine.set_linewidth(0.7)
    ax.set_xlabel('X (mm)', fontsize=7.5, color=MUTED)
    ax.set_ylabel('Y (mm)', fontsize=7.5, color=MUTED)
    draw_jaws(ax, show_dims)

def mlc_line(ax, x, color=ACCENT, lw=4.5, alpha=1.0, z=5):
    ax.plot([x, x], [Y1, Y2], color=color, linewidth=lw,
            solid_capstyle='butt', zorder=z, alpha=alpha)

def tx_strip(ax, x_center, color=TX_COLOR, z=5, alpha=1.0):
    ax.add_patch(Rectangle((x_center - TX_WIDTH/2, Y1), TX_WIDTH, Y2 - Y1,
                           facecolor=color, edgecolor='none',
                           zorder=z, alpha=alpha))

# ============================================================
# Figure
# ============================================================
fig, axes = plt.subplots(2, 2, figsize=(13, 13.5))
fig.patch.set_facecolor('white')

fig.suptitle('DLG QA Plan Generator — Field Geometry',
             fontsize=15, fontweight='bold', color=NAVY, y=0.985)
fig.text(0.5, 0.965,
         'Reference fields and sweeping gap configuration  |  v0.9.0',
         ha='center', fontsize=9, color=MUTED, style='italic')

# ------------------------------------------------------------
# Panel A — Full overview
# ------------------------------------------------------------
ax = axes[0, 0]
setup_panel(ax, 'A  ·  Full field overview', show_dims=True)

mlc_line(ax, X1 - OPEN_PAD, color=ACCENT, lw=5)
mlc_line(ax, X2 + OPEN_PAD, color=ACCENT, lw=5)
tx_strip(ax, X1 - TX_OVERREACH)
tx_strip(ax, X2 + TX_OVERREACH)

# Central annotation
ax.text(0, 0, 'Jaw aperture\n100 × 180 mm',
        ha='center', va='center', fontsize=9.5,
        color=NAVY, style='italic', zorder=6,
        bbox=dict(boxstyle='round,pad=0.45',
                  facecolor='white', edgecolor=NAVY,
                  linewidth=0.8, alpha=0.95))

# X1/X2 labels
ax.annotate(f'X1 = {int(X1)}', xy=(X1, 0), xytext=(X1 - 12, -78),
            fontsize=7.5, color=JAW_EDGE, ha='center',
            arrowprops=dict(arrowstyle='->', color=JAW_EDGE, lw=0.7))
ax.annotate(f'X2 = +{int(X2)}', xy=(X2, 0), xytext=(X2 + 12, -78),
            fontsize=7.5, color=JAW_EDGE, ha='center',
            arrowprops=dict(arrowstyle='->', color=JAW_EDGE, lw=0.7))

# ------------------------------------------------------------
# Panel B — OPEN reference
# ------------------------------------------------------------
ax = axes[0, 1]
setup_panel(ax, 'B  ·  OPEN reference')

mlc_line(ax, X1 - OPEN_PAD, color=ACCENT, lw=6)
mlc_line(ax, X2 + OPEN_PAD, color=ACCENT, lw=6)

# Hidden sweep arrows
ax.annotate('', xy=(X1 - OPEN_PAD - 4, -50), xytext=(X1 - OPEN_PAD, -50),
            arrowprops=dict(arrowstyle='->', color=ACCENT, lw=1.8))
ax.annotate('', xy=(X2 + OPEN_PAD + 4, -50), xytext=(X2 + OPEN_PAD, -50),
            arrowprops=dict(arrowstyle='->', color=ACCENT, lw=1.8))

ax.text(X1 - OPEN_PAD, 65, 'Bank 0', ha='center', va='center',
        fontsize=8.5, color=ACCENT, fontweight='bold')
ax.text(X2 + OPEN_PAD, 65, 'Bank 1', ha='center', va='center',
        fontsize=8.5, color=ACCENT, fontweight='bold')

ax.text(0, -78,
        'Hidden sweep: 2 mm\n(outside jaws — no effect on aperture)',
        ha='center', va='center', fontsize=7.8,
        color=ACCENT_DARK, style='italic',
        bbox=dict(boxstyle='round,pad=0.35',
                  facecolor='#F0F7FB', edgecolor=ACCENT,
                  linewidth=0.6))

# ------------------------------------------------------------
# Panel C — Transmission A / B
# ------------------------------------------------------------
ax = axes[1, 0]
setup_panel(ax, 'C  ·  Transmission A / B')

tx_strip(ax, X2 + TX_OVERREACH, color=TX_COLOR, alpha=1.0)
tx_strip(ax, X1 - TX_OVERREACH, color=TX_COLOR, alpha=1.0)

ax.annotate('TX A\n1 mm strip\n@ X2 + 10 mm',
            xy=(X2 + TX_OVERREACH, 20),
            xytext=(X2 + TX_OVERREACH + 12, 55),
            fontsize=7.8, color=TX_COLOR, ha='left',
            fontweight='bold',
            arrowprops=dict(arrowstyle='->', color=TX_COLOR,
                            lw=0.9, connectionstyle='arc3,rad=0.2'))
ax.annotate('TX B\n1 mm strip\n@ X1 − 10 mm',
            xy=(X1 - TX_OVERREACH, -20),
            xytext=(X1 - TX_OVERREACH - 12, -55),
            fontsize=7.8, color=TX_COLOR, ha='right',
            fontweight='bold',
            arrowprops=dict(arrowstyle='->', color=TX_COLOR,
                            lw=0.9, connectionstyle='arc3,rad=-0.2'))

# ------------------------------------------------------------
# Panel D — DLG sweeping gap
# ------------------------------------------------------------
ax = axes[1, 1]
setup_panel(ax, 'D  ·  DLG sweeping gap')

positions = [-60, -30, 0, 30, 60]
alphas    = [0.15, 0.35, 1.0, 0.35, 0.15]
for pos, a in zip(positions, alphas):
    b0 = pos - GAP_DEMO / 2
    b1 = pos + GAP_DEMO / 2
    mlc_line(ax, b0, color=ACCENT, lw=2.8, alpha=a)
    mlc_line(ax, b1, color=ACCENT, lw=2.8, alpha=a)

# Sweep arrows
ax.annotate('', xy=(SWEEP_END, -88), xytext=(SWEEP_START, -88),
            arrowprops=dict(arrowstyle='->', color=NAVY, lw=1.8))
ax.annotate('', xy=(SWEEP_START, -88), xytext=(SWEEP_END, -88),
            arrowprops=dict(arrowstyle='->', color=NAVY, lw=1.8))
ax.text(0, -100, 'Sweep −70 → +70 mm   (11 control points)',
        ha='center', fontsize=8.5, color=NAVY, fontweight='bold')

ax.annotate('Gap = 2–20 mm',
            xy=(0, 30), xytext=(28, 68),
            fontsize=8.5, color=ACCENT_DARK, fontweight='bold',
            arrowprops=dict(arrowstyle='->', color=ACCENT_DARK, lw=1.0))

# ============================================================
# Global legend
# ============================================================
legend_handles = [
    Line2D([0], [0], color=ACCENT, lw=4.5, label='MLC banks'),
    Line2D([0], [0], color=TX_COLOR, lw=4.5, label='TX strips'),
    Line2D([0], [0], color=JAW_EDGE, lw=2.0, label='X jaws'),
]
fig.legend(handles=legend_handles,
           loc='lower center', ncol=3, frameon=False,
           bbox_to_anchor=(0.5, 0.005),
           fontsize=9)

plt.tight_layout(pad=2.4, rect=[0, 0.025, 1, 0.96])
plt.savefig('diagram.png', dpi=300, bbox_inches='tight', facecolor='white')
plt.show()
print('✓ Saved: diagram.png  (300 DPI)')
