<script setup lang="ts">
// The List's first-load skeleton (8.7): a column header, 2 group headers and 8 rows, laid out with
// the table's own column widths so nothing jumps when the tasks arrive. Shown only after 200ms
// (8.1). Phones get the two-line rows and no column header.
import UiSkeleton from '../ui/UiSkeleton.vue'

const GROUPS = [
  { label: '88px', rows: ['46%', '62%', '38%', '55%', '30%'] },
  { label: '112px', rows: ['58%', '42%', '50%'] },
]
</script>

<template>
  <div class="list-skeleton" aria-hidden="true">
    <div class="line header">
      <span class="gutter" />
      <span class="name"><span class="bar"><UiSkeleton width="40px" /></span></span>
      <span class="col col--assignees"><UiSkeleton width="64px" /></span>
      <span class="col col--due"><UiSkeleton width="56px" /></span>
      <span class="col col--priority">
        <span class="wide"><UiSkeleton width="52px" /></span>
        <span class="narrow"><UiSkeleton width="16px" /></span>
      </span>
      <span class="col col--tags"><UiSkeleton width="36px" /></span>
      <span class="menu" />
    </div>
    <template v-for="g in GROUPS" :key="g.label">
      <div class="line group">
        <span class="gutter" />
        <span class="name">
          <span class="chevron" />
          <span class="glyph"><UiSkeleton variant="circle" width="16px" height="16px" /></span>
          <span class="bar"><UiSkeleton :width="g.label" /></span>
        </span>
      </div>
      <div v-for="(w, i) in g.rows" :key="i" class="line row">
        <span class="gutter" />
        <span class="name">
          <span class="chevron" />
          <span class="glyph"><UiSkeleton variant="circle" width="16px" height="16px" /></span>
          <span class="title">
            <UiSkeleton :width="w" />
            <span class="second"><UiSkeleton width="40%" /></span>
          </span>
        </span>
        <span class="col col--assignees"><UiSkeleton variant="circle" width="24px" height="24px" /></span>
        <span class="col col--due"><UiSkeleton width="56px" /></span>
        <span class="col col--priority">
          <span class="wide"><UiSkeleton width="52px" /></span>
          <span class="narrow"><UiSkeleton width="16px" /></span>
        </span>
        <span class="col col--tags"><UiSkeleton width="48px" /></span>
        <span class="menu" />
      </div>
    </template>
  </div>
</template>

<style scoped>
/* The same widths as ListTable's columns. */
.list-skeleton {
  display: flex;
  flex-direction: column;
}

.line {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--border-subtle);
}

.header {
  height: 32px;
}

.group {
  height: 40px;
}

.row {
  height: var(--row-list);
}

.gutter {
  flex: none;
  width: 40px;
}

.name {
  display: flex;
  flex: 1 1 0;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 280px;
  padding-right: var(--space-3);
}

.header .name {
  padding-left: calc(20px + var(--space-1-5) + var(--control-xs) + var(--space-1-5));
}

.chevron {
  flex: none;
  width: 20px;
}

.glyph {
  display: flex;
  flex: none;
  justify-content: center;
  width: var(--control-xs);
}

.bar {
  display: block;
  flex: none;
  width: 160px;
}

.title {
  display: block;
  flex: 1 1 auto;
  min-width: 0;
}

.second {
  display: none;
}

.col {
  display: flex;
  flex: none;
  align-items: center;
  width: 112px;
  padding: 0 var(--space-1-5);
}

.col--tags {
  width: 160px;
}

.menu {
  flex: none;
  width: 40px;
}

.wide {
  display: block;
  width: 100%;
}

.narrow {
  display: none;
  width: 100%;
}

@container page (width < 900px) {
  .col--tags {
    display: none;
  }

  .name {
    min-width: 200px;
  }
}

@container page (width < 720px) {
  .col--priority {
    width: 48px;
  }

  .wide {
    display: none;
  }

  .narrow {
    display: block;
  }
}

/* Phones: two-line rows and no column header (8.7). */
@container page (width < 560px) {
  .header,
  .gutter,
  .col,
  .menu {
    display: none;
  }

  .line {
    padding-left: var(--space-3);
  }

  .name {
    min-width: 0;
  }

  .row {
    height: 56px;
  }

  .row .glyph {
    align-self: flex-start;
    margin-top: var(--space-2);
  }

  .second {
    display: block;
  }
}
</style>
