export function unixSeconds(date=new Date()){return Math.floor(date.getTime()/1000);}
export function dayKey(date=new Date()){return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;}
export function clamp(value:number,min:number,max:number){return Math.min(max,Math.max(min,value));}
export function errorMessage(value:unknown){return value instanceof Error?value.message:String(value);}
