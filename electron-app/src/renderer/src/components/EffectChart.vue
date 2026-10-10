<script setup lang="ts">
import { computed } from 'vue'
import type { EffectPoint } from '@shared/types'

const props = defineProps<{
  series: EffectPoint[]
  /** Direct baseline latency in ms, or null when unknown. */
  baseline: number | null
}>()

// Plot geometry in viewBox units. The SVG stretches horizontally (preserveAspectRatio="none") while the
// labels stay in HTML so they never distort.
const W = 600
const H = 160
const PAD_L = 4
const PAD_R = 4
const PAD_T = 10
const PAD_B = 10
const PLOT_W = W - PAD_L - PAD_R
const PLOT_H = H - PAD_T - PAD_B

// Show the last ~3 minutes of samples.
const WINDOW_MS = 3 * 60 * 1000

const visible = computed<EffectPoint[]>(() => {
  if (props.series.length === 0) return []
  const last = props.series[props.series.length - 1].ts
  const cutoff = last - WINDOW_MS
  return props.series.filter((p) => p.ts >= cutoff)
})

const maxY = computed(() => {
  const ok = visible.value.filter((p) => p.accel >= 0).map((p) => p.accel)
  const base = props.baseline ?? 0
  const max = Math.max(base, ...ok, 1)
  return Math.max(10, Math.round(max * 1.2))
})

const gridLines = computed(() => {
  const m = maxY.value
  return [m, Math.round((m * 2) / 3), Math.round(m / 3)]
})

function xOf(i: number): number {
  const n = Math.max(visible.value.length - 1, 1)
  return PAD_L + (i / n) * PLOT_W
}

function yOf(v: number): number {
  const clamped = Math.max(0, Math.min(v, maxY.value))
  return PAD_T + PLOT_H - (clamped / maxY.value) * PLOT_H
}

// Build the polyline, breaking it at failures (negative samples) so they render as gaps.
const segments = computed<{ d: string; failures: { x: number; y: number }[] }>(() => {
  const pts = visible.value
  const failures: { x: number; y: number }[] = []
  let d = ''
  let pen = false
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i]
    if (p.accel < 0) {
      pen = false
      failures.push({ x: xOf(i), y: PAD_T + PLOT_H - 3 })
      continue
    }
    const x = xOf(i)
    const y = yOf(p.accel)
    d += pen ? ` L ${x.toFixed(1)} ${y.toFixed(1)}` : `M ${x.toFixed(1)} ${y.toFixed(1)}`
    pen = true
  }
  return { d, failures }
})

const baselineY = computed(() => (props.baseline != null && props.baseline >= 0 ? yOf(props.baseline) : null))
</script>

<template>
  <div class="chart">
    <div v-if="visible.length === 0" class="chart-empty muted">加速后开始记录</div>
    <template v-else>
      <div class="chart-body">
        <div class="y-labels">
          <span v-for="g in gridLines" :key="g">{{ g }} ms</span>
        </div>
        <svg :viewBox="`0 0 ${W} ${H}`" preserveAspectRatio="none" class="plot">
          <line
            v-for="g in gridLines"
            :key="g"
            :x1="PAD_L"
            :x2="W - PAD_R"
            :y1="yOf(g)"
            :y2="yOf(g)"
            class="grid"
          />
          <line
            v-if="baselineY != null"
            :x1="PAD_L"
            :x2="W - PAD_R"
            :y1="baselineY"
            :y2="baselineY"
            class="baseline"
          />
          <path :d="segments.d" class="line" />
          <line
            v-for="(f, i) in segments.failures"
            :key="i"
            :x1="f.x"
            :x2="f.x"
            :y1="f.y - 6"
            :y2="f.y"
            class="fail"
          />
        </svg>
      </div>
      <div class="x-labels muted">
        <span>3 分钟前</span>
        <span v-if="baseline != null" class="baseline-label">直连 {{ baseline }} ms</span>
        <span>现在</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.chart {
  margin-top: 14px;
}
.chart-empty {
  height: 120px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  border: 1px dashed var(--border);
  border-radius: var(--radius);
}
.chart-body {
  display: flex;
  gap: 8px;
}
.y-labels {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  font-size: 11px;
  color: var(--text-3);
  padding: 2px 0;
  min-width: 44px;
  text-align: right;
}
.plot {
  flex: 1;
  height: 160px;
  overflow: visible;
}
.grid {
  stroke: var(--border);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}
.baseline {
  stroke: var(--text-3);
  stroke-width: 1;
  stroke-dasharray: 4 4;
  vector-effect: non-scaling-stroke;
}
.line {
  fill: none;
  stroke: var(--brand);
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}
.fail {
  stroke: var(--danger);
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}
.x-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-top: 4px;
  padding-left: 52px;
}
.baseline-label {
  color: var(--text-3);
}
</style>
