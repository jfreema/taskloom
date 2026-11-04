// The columns of the /_ui state tables (10.4): every state a primitive or field can show.
export const STATES = ['default', 'hover', 'focus', 'disabled', 'loading', 'invalid', 'empty'] as const

export type SampleState = (typeof STATES)[number]

export const STATE_LABEL: Readonly<Record<SampleState, string>> = {
  default: 'Default',
  hover: 'Hover',
  focus: 'Focus',
  disabled: 'Disabled',
  loading: 'Loading',
  invalid: 'Invalid',
  empty: 'Empty',
}
