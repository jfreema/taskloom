<script setup lang="ts">
// Form field frame (spec 7.10): a label, the control from the default slot, a hint and an error.
// The slot receives { id, describedBy, invalid, required } to bind on the control. The error sits
// under the control in --danger-text with a CircleAlert and is linked through aria-describedby.
import { computed, useId } from 'vue'
import { CircleAlert } from '@lucide/vue'
import UiIcon from './UiIcon.vue'

const props = withDefaults(
  defineProps<{
    label: string
    hint?: string
    error?: string
    required?: boolean
    /** The label stays for screen readers only. */
    labelHidden?: boolean
  }>(),
  { hint: undefined, error: undefined, required: false, labelHidden: false },
)

const id = useId()
const hintId = `${id}-hint`
const errorId = `${id}-error`
const describedBy = computed(() => [props.hint ? hintId : '', props.error ? errorId : ''].filter(Boolean).join(' ') || undefined)
const invalid = computed(() => !!props.error)
</script>

<template>
  <div class="ui-field" :class="{ 'is-invalid': invalid }">
    <label class="label" :class="{ 'sr-only': labelHidden }" :for="id">
      {{ label }}<span v-if="required" class="required" aria-hidden="true">*</span>
    </label>
    <slot :id="id" :described-by="describedBy" :invalid="invalid" :required="required" />
    <p v-if="hint" :id="hintId" class="hint">{{ hint }}</p>
    <p v-if="error" :id="errorId" class="error">
      <UiIcon :icon="CircleAlert" :size="14" />
      <span>{{ error }}</span>
    </p>
  </div>
</template>

<style scoped>
.ui-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  min-width: 0;
}

.label {
  align-self: flex-start;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.required {
  margin-left: var(--space-0-5);
  color: var(--text-tertiary);
}

.hint {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.error {
  display: flex;
  align-items: flex-start;
  gap: var(--space-1);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--danger-text);
}

.error :deep(.ui-icon) {
  margin-top: 1px;
}
</style>
