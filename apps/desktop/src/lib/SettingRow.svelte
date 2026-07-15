<script lang="ts">import type {Snippet} from 'svelte';let{title,desc,children}:{title:string;desc:string;children:Snippet}=$props();</script>
<div class="setting-row"><div><strong>{title}</strong><p>{desc}</p></div>{@render children()}</div>
