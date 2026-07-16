/** Small, dependency-free Markdown renderer for chat output. All source text is escaped first. */
export function renderMarkdown(source: string): string {
  const codeBlocks: string[] = [];
  const masked = (source || '').replace(/\x60{3}[^\n]*\n([\s\S]*?)\x60{3}/g, (_match, code: string) => {
    const index = codeBlocks.push(code.replace(/\n$/, '')) - 1;
    return '\n\u0000CODE' + index + '\u0000\n';
  });
  const lines = escapeHtml(masked).split('\n');
  const out: string[] = [];
  let list: 'ul' | 'ol' | null = null;

  const closeList = () => {
    if (!list) return;
    out.push('</' + list + '>');
    list = null;
  };

  for (const line of lines) {
    const code = line.match(/^\u0000CODE(\d+)\u0000$/);
    if (code) {
      closeList();
      out.push('<pre class="md-code"><code>' + escapeHtml(codeBlocks[Number(code[1])] || '') + '</code></pre>');
      continue;
    }
    const bullet = line.match(/^[-*]\s+(.+)$/);
    const ordered = line.match(/^\d+[.)]\s+(.+)$/);
    if (bullet || ordered) {
      const next = bullet ? 'ul' : 'ol';
      if (list !== next) {
        closeList();
        list = next;
        out.push('<' + next + ' class="md-list">');
      }
      out.push('<li>' + formatInline((bullet || ordered)![1]) + '</li>');
      continue;
    }
    closeList();
    if (/^###\s+/.test(line)) out.push('<h3 class="md-h">' + formatInline(line.replace(/^###\s+/, '')) + '</h3>');
    else if (/^##\s+/.test(line)) out.push('<h2 class="md-h">' + formatInline(line.replace(/^##\s+/, '')) + '</h2>');
    else if (/^#\s+/.test(line)) out.push('<h1 class="md-h">' + formatInline(line.replace(/^#\s+/, '')) + '</h1>');
    else if (/^&gt;\s+/.test(line)) out.push('<blockquote class="md-quote">' + formatInline(line.replace(/^&gt;\s+/, '')) + '</blockquote>');
    else if (!line.trim()) out.push('<br/>');
    else out.push('<p class="md-p">' + formatInline(line) + '</p>');
  }
  closeList();
  return out.join('');
}

function formatInline(text: string): string {
  return text
    .replace(/\x60([^\x60\n]+)\x60/g, '<code class="md-inline">$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>');
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
