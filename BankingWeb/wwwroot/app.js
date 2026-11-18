const form = document.querySelector('#request-form');
const input = document.querySelector('#input');
const messages = document.querySelector('#messages');
const send = document.querySelector('#send');
const balanceStatus = document.querySelector('#balance-status');
let sessionReady = false;

function updateBalances(balances) {
  if (!balances) return;
  document.querySelector('#savings-balance').textContent = balances.savings;
  document.querySelector('#chequing-balance').textContent = balances.chequing;
  document.querySelector('#dock-savings').textContent = balances.savings;
  document.querySelector('#dock-chequing').textContent = balances.chequing;
  balanceStatus.textContent = 'Updated from your current session';
}

async function initializeSession() {
  try {
    const response = await fetch('/api/session', { cache: 'no-store' });
    if (!response.ok) throw new Error('Session unavailable');
    updateBalances((await response.json()).balances);
    sessionReady = true;
    send.disabled = false;
  } catch {
    balanceStatus.textContent = 'Unable to load balances. Refresh to try again.';
  }
}

initializeSession();

function updateBalanceDock() {
  const cards = document.querySelector('.accounts');
  const dock = document.querySelector('#balance-dock');
  dock.classList.toggle('visible', window.innerWidth <= 800 && cards.getBoundingClientRect().bottom < 0);
}
window.addEventListener('scroll', updateBalanceDock, { passive: true });
window.addEventListener('resize', updateBalanceDock);
updateBalanceDock();

function addMessage(label, text, kind) {
  const wrapper = document.createElement('div');
  wrapper.className = `message ${kind}`;
  const heading = document.createElement('span');
  heading.className = 'message-label';
  heading.textContent = label;
  const bubble = document.createElement('div');
  bubble.className = 'bubble';
  bubble.textContent = text;
  wrapper.append(heading, bubble);
  messages.append(wrapper);
  messages.scrollTop = messages.scrollHeight;
  return wrapper;
}

function addTrace(wrapper, result) {
  const details = document.createElement('details');
  details.className = 'trace';
  const summary = document.createElement('summary');
  summary.textContent = 'View processing details';
  details.append(summary);
  const lines = [];
  if (result.command) lines.push(`Interpreted command: ${result.command}`);
  if (result.simulatorResult) lines.push(`Simulator: ${result.simulatorResult}`);
  const stages = {
    original_command: 'Original command sent directly to the simulator.',
    original_explained: 'Original command executed by the simulator; conversational response accepted.',
    original_reply_error: 'Original command executed once; response service failed, so the simulator result is shown.',
    original_fallback: 'Original command executed once; the original simulator result is shown.',
    processed: 'Interpretation validated; simulator executed once; response accepted.',
    clarification: 'Details were unclear or incomplete; no operation was executed.',
    confirmation_required: 'Please confirm the interpreted command; no operation has been executed yet.',
    cancelled: 'The pending request was cancelled; no operation was executed.',
    configuration_error: 'AI configuration is missing; no operation was executed.',
    interpretation_error: 'Interpretation failed; no operation was executed.',
    reply_error: 'Response generation failed; the original simulator result is shown.',
    reply_fallback: 'Response did not match the simulator result; the original result is shown.'
  };
  lines.push(stages[result.stage] ?? 'Processing details unavailable.');
  for (const line of lines) { const p = document.createElement('p'); p.textContent = line; details.append(p); }
  wrapper.append(details);
}

document.querySelectorAll('[data-example]').forEach(button => {
  button.addEventListener('click', () => { input.value = button.dataset.example; input.focus(); });
});

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (!sessionReady) return;
  const text = input.value.trim();
  if (!text) return;
  input.value = '';
  addMessage('YOU', text, 'user');
  send.disabled = true;
  try {
    const response = await fetch('/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ input: text }) });
    const result = await response.json();
    if (!response.ok) throw new Error('Request failed');
    updateBalances(result.balances);
    const message = addMessage('ASSISTANT', result.output ?? result.error ?? 'Something went wrong.', 'assistant');
    if (result.command && !result.stage.startsWith('original_')) {
      const understood = document.createElement('p');
      understood.className = 'understood';
      understood.textContent = `Understood as: ${result.command}`;
      message.append(understood);
    }
    addTrace(message, result);
    if (result.ended) {
      sessionReady = false;
      addMessage('ASSISTANT', 'Your session has ended. A new simulated session is ready.', 'assistant');
      await initializeSession();
    }
  } catch {
    addMessage('ASSISTANT', 'The service is unavailable. Please check whether the server is running.', 'assistant');
  } finally { send.disabled = !sessionReady; input.focus(); }
});
