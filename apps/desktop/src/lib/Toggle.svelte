<script lang="ts">let{checked=false,disabled=false,label='切换此设置',onChange}:{checked?:boolean;disabled?:boolean;label?:string;onChange?:(value:boolean)=>void}=$props();</script>
<label class="switch" aria-label={label}><input type="checkbox" {checked} {disabled} aria-label={label} onchange={event=>onChange?.(event.currentTarget.checked)}><i></i></label>
