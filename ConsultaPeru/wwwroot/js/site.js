const form = document.querySelector('#consulta-form');
if (form) {
 const numero = form.querySelector('#Numero');
 const limpiar = form.querySelector('#limpiar-numero');
 const actualizarLimpiar = () => { if (limpiar) limpiar.hidden = numero.value.length === 0; };
 limpiar?.addEventListener('click', () => {
  numero.value = '';
  numero.dispatchEvent(new Event('input', { bubbles: true }));
  numero.focus({ preventScroll: true });
 });
 actualizarLimpiar();
 form.querySelectorAll('[name="Tipo"]').forEach(radio => radio.addEventListener('change', () => {
 const length = radio.value === 'RUC' ? 11 : 8;
 numero.maxLength = length; numero.pattern = `[0-9]{${length}}`;
 numero.placeholder = `Ingresa los ${length} dígitos`; numero.value = '';
 actualizarLimpiar();
 document.querySelector('#ayuda').textContent = `${length} dígitos, sin espacios ni guiones.`; numero.focus();
 }));
 numero.addEventListener('input', () => { numero.value = numero.value.replace(/[^0-9]/g, ''); actualizarLimpiar(); });
 form.addEventListener('submit', () => { const button = form.querySelector('button[type="submit"]'); button.disabled = true; button.textContent = 'Consultando…'; });
 window.addEventListener('pageshow', event => {
  const button = form.querySelector('button[type="submit"]');
  button.disabled = false;
  button.textContent = 'Buscar documento →';
  actualizarLimpiar();
  // Al volver con Atrás, conserva el desplazamiento que restaura el navegador.
  if (event.persisted) return;
  const area = document.querySelector('#consulta-area');
  if (area?.dataset.hasResult !== 'true') {
   numero.focus({ preventScroll: true });
  }
 });
}

// Mismo desplazamiento para DNI/RUC y tipo de cambio.
window.addEventListener('pageshow', event => {
 if (event.persisted) return;
 const area = document.querySelector('.workspace[data-has-result="true"]');
 if (!area) return;
 requestAnimationFrame(() => {
  const target = window.matchMedia('(max-width: 700px)').matches
   ? area.querySelector('[data-result-panel]') : area;
  if (!target) return;
  const headerHeight = document.querySelector('header')?.getBoundingClientRect().height ?? 0;
  const top = target.getBoundingClientRect().top + window.scrollY - headerHeight - 6;
  window.scrollTo({
   top: Math.max(0, top),
   behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'
  });
 });
});

document.querySelectorAll('[data-copy]').forEach(button => {
 button.addEventListener('click', async () => {
  const status = document.querySelector('#copy-status');
  try {
   if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
   await navigator.clipboard.writeText(button.dataset.copy);
   button.textContent = 'Copiado';
   if (status) status.textContent = `${button.getAttribute('aria-label').replace('Copiar ', '')}: copiado al portapapeles.`;
   setTimeout(() => { button.textContent = 'Copiar'; }, 1800);
  } catch {
   if (status) status.textContent = 'No se pudo acceder al portapapeles. Selecciona el texto y cópialo manualmente.';
  }
 });
});
document.querySelectorAll('.loading-form').forEach(form => {
 const button = form.querySelector('button[type=submit]');
 const original = button.textContent;
 form.addEventListener('submit', () => { button.disabled = true; button.textContent = 'Consultando…'; });
 window.addEventListener('pageshow', () => { button.disabled = false; button.textContent = original; });
});

// Acceso al panel privado; la protección real se valida en el servidor.
document.addEventListener('keydown', event => {
 if (event.ctrlKey && event.altKey && !event.shiftKey && event.code === 'KeyA' && !event.repeat) {
  event.preventDefault();
  window.location.assign('/Actividad');
 }
});
