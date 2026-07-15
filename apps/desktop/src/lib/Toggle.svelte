<script lang="ts">let{checked=false,disabled=false,onChange}:{checked?:boolean;disabled?:boolean;onChange?:(value:boolean)=>void}=$props();</script>
<label class="switch"><input type="checkbox" {checked} {disabled} onchange={event=>onChange?.(event.currentTarget.checked)}><i></i></label>
