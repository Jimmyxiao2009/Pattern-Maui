/** Minimal markdown → safe HTML for assistant messages (no external deps). */
export function renderMarkdown(source: string): string {
  const escaped = escapeHtml(source || '');
  const withCode = escaped.replace(/```([\w-]*)\n([\s\S]*?)```/g, (_m, _lang, code) => {
    return `<pre class="md-code"><code>${code.replace(/\n$/, '')}</code></pre>`;
  });
  const withInline = withCode.replace(/`([^`\n]+)`/g, '<code class="md-inline">$1</code>');
  const lines = withInline.split('\n');
  const out: string[] = [];
  let inList = false;
  for (const line of lines) {
    if (line.startsWith('<pre')) {
      if (inList) {
        out.push('</ul>');
        inList = false;
      }
      out.push(line);
      continue;
    }
    const bullet = line.match(/^[-*]\s+(.+)$/);
    if (bullet) {
      if (!inList) {
        out.push('<ul class="md-list">');
        inList = true;
      }
      out.push(`<li>${formatInline(bullet[1])}</li>`);
      continue;
    }
    if (inList) {
      out.push('</ul>');
      inList = false;
    }
    if (/^###\s+/.test(line)) out.push(`<h3 class="md-h">${formatInline(line.replace(/^###\s+/, ''))}</h3>`);
    else if (/^##\s+/.test(line)) out.push(`<h2 class="md-h">${formatInline(line.replace(/^##\s+/, ''))}</h2>`);
    else if (/^#\s+/.test(line)) out.push(`<h1 class="md-h">${formatInline(line.replace(/^#\s+/, ''))}</h1>`);
    else if (line.trim() === '') out.push('<br/>');
    else out.push(`<p class="md-p">${formatInline(line)}</p>`);
  }
  if (inList) out.push('</ul>');
  return out.join('');
}

function formatInline(text: string): string {
  return text
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>');
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
