import { useState } from 'react'
import type { Recipe, RecipeParameter } from '../types'
import { useStore } from '../store'
import { call } from '../bridge'
import { Button, IconButton, Label, TextArea, TextInput } from './ui'

interface Props {
  recipe: Recipe | null
  onClose: () => void
  onSaved: (r: Recipe) => void
}

interface ParamDraft {
  name: string
  optionsText: string
  defaultValue: string
}

export function RecipeEditorModal({ recipe, onClose, onSaved }: Props) {
  const { reload } = useStore()
  const editing = !!recipe

  const [icon, setIcon] = useState(recipe?.icon ?? '✦')
  const [name, setName] = useState(recipe?.name ?? '')
  const [systemPrompt, setSystemPrompt] = useState(recipe?.systemPrompt ?? '')
  const [template, setTemplate] = useState(recipe?.userPromptTemplate ?? '{input}')
  const [paramDrafts, setParamDrafts] = useState<ParamDraft[]>(
    (recipe?.parameters ?? []).map((p) => ({
      name: p.name,
      optionsText: p.options.join('\n'),
      defaultValue: p.defaultValue,
    })),
  )
  const [error, setError] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  function addParam() {
    setParamDrafts((d) => [...d, { name: '', optionsText: 'Option1\nOption2', defaultValue: 'Option1' }])
  }

  function updateParam(i: number, patch: Partial<ParamDraft>) {
    setParamDrafts((d) => d.map((p, idx) => (idx === i ? { ...p, ...patch } : p)))
  }

  function removeParam(i: number) {
    setParamDrafts((d) => d.filter((_, idx) => idx !== i))
  }

  async function save() {
    if (!name.trim()) return setError('Prompt name is required.')
    if (!template.trim()) return setError('Prompt template is required.')

    const parameters: RecipeParameter[] = paramDrafts
      .filter((p) => p.name.trim())
      .map((p) => ({
        name: p.name.trim(),
        options: p.optionsText.split('\n').map((s) => s.trim()).filter(Boolean),
        defaultValue: p.defaultValue.trim(),
      }))

    const result: Recipe = {
      id: recipe?.id ?? crypto.randomUUID(),
      name: name.trim(),
      icon: icon.trim() || '✦',
      systemPrompt: systemPrompt.trim(),
      userPromptTemplate: template.trim(),
      parameters,
      isBuiltIn: recipe?.isBuiltIn ?? false,
      sortOrder: recipe?.sortOrder ?? 999,
    }

    await call('saveRecipe', { recipe: result })
    await reload()
    onSaved(result)
  }

  async function doDelete() {
    if (!recipe) return
    await call('deleteRecipe', { id: recipe.id })
    await reload()
    onClose()
  }

  return (
    <div className="absolute inset-0 z-50 grid place-items-center bg-black/40 p-6 animate-fade-in" onMouseDown={onClose}>
      <div
        className="flex max-h-full w-[520px] flex-col overflow-hidden rounded-xl border border-border bg-bg shadow-pop"
        onMouseDown={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <span className="text-sm font-semibold">{editing ? 'Edit Prompt' : 'New Prompt'}</span>
          <IconButton onClick={onClose}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
            </svg>
          </IconButton>
        </div>

        {/* Body */}
        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-4">
          {error && (
            <div className="rounded-lg border border-danger/30 bg-danger-soft px-3 py-2 text-xs text-danger">{error}</div>
          )}

          <div className="flex gap-3">
            <div className="w-20 space-y-1.5">
              <Label>Icon</Label>
              <TextInput value={icon} onChange={(e) => setIcon(e.target.value)} maxLength={4} className="text-center text-lg" />
            </div>
            <div className="flex-1 space-y-1.5">
              <Label>Name</Label>
              <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Commit Message" />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>System prompt</Label>
            <TextArea value={systemPrompt} onChange={(e) => setSystemPrompt(e.target.value)} rows={3}
              placeholder="Instructions to the AI…" />
          </div>

          <div className="space-y-1.5">
            <Label>Template</Label>
            <TextArea value={template} onChange={(e) => setTemplate(e.target.value)} rows={4}
              placeholder="{input} = selected text, {param:Name} = a parameter value" className="font-mono text-xs" />
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label>Parameters</Label>
              <Button size="sm" onClick={addParam}>＋ Add Parameter</Button>
            </div>
            {paramDrafts.map((p, i) => (
              <div key={i} className="flex gap-2 rounded-lg border border-border bg-elevated p-3">
                <div className="flex-1 space-y-2">
                  <TextInput value={p.name} onChange={(e) => updateParam(i, { name: e.target.value })} placeholder="Parameter name" />
                  <TextArea value={p.optionsText} onChange={(e) => updateParam(i, { optionsText: e.target.value })} rows={3}
                    placeholder="Options (one per line)" className="text-xs" />
                  <TextInput value={p.defaultValue} onChange={(e) => updateParam(i, { defaultValue: e.target.value })} placeholder="Default value" />
                </div>
                <IconButton onClick={() => removeParam(i)}>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
                  </svg>
                </IconButton>
              </div>
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-border px-4 py-3">
          <div>
            {editing && !recipe?.isBuiltIn && !confirmDelete && (
              <Button variant="subtle" className="text-danger hover:bg-danger-soft" onClick={() => setConfirmDelete(true)}>
                Delete
              </Button>
            )}
            {confirmDelete && (
              <div className="flex items-center gap-2">
                <span className="text-xs text-text-muted">Delete this prompt?</span>
                <Button size="sm" variant="danger" onClick={doDelete}>Delete</Button>
                <Button size="sm" onClick={() => setConfirmDelete(false)}>Cancel</Button>
              </div>
            )}
          </div>
          <div className="flex gap-2">
            <Button onClick={onClose}>Cancel</Button>
            <Button variant="primary" onClick={save}>Save</Button>
          </div>
        </div>
      </div>
    </div>
  )
}
