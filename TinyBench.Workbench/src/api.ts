import type { DriverDefinition, ParameterDocument, RunSnapshot } from './types';

export async function getDefinition(hostUrl: string): Promise<DriverDefinition> {
  const response = await fetch(joinUrl(hostUrl, 'v1/definition'));
  await ensureOk(response);
  return response.json();
}

export async function connectDevice(
  hostUrl: string,
  parameters: Record<string, unknown>,
): Promise<void> {
  const response = await fetch(joinUrl(hostUrl, 'v1/connection/connect'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ parameters }),
  });
  await ensureOk(response);
}

export async function disconnectDevice(hostUrl: string): Promise<void> {
  const response = await fetch(joinUrl(hostUrl, 'v1/connection/disconnect'), {
    method: 'POST',
  });
  await ensureOk(response);
}

export async function startRun(
  hostUrl: string,
  commandId: string,
  inputs: Record<string, unknown>,
): Promise<RunSnapshot> {
  const response = await fetch(joinUrl(hostUrl, `v1/commands/${encodeURIComponent(commandId)}/runs`), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ inputs }),
  });
  await ensureOk(response);
  return response.json();
}

export async function getRun(hostUrl: string, runId: string): Promise<RunSnapshot> {
  const response = await fetch(joinUrl(hostUrl, `v1/runs/${encodeURIComponent(runId)}`));
  await ensureOk(response);
  return response.json();
}

export function parseInput(parameter: ParameterDocument, rawValue: string): unknown {
  if (!parameter.required && rawValue.trim() === '') {
    return undefined;
  }

  if (parameter.array) {
    const values = rawValue.split(',').map((item) => item.trim()).filter(Boolean);
    return values.map((value) => parseScalar(parameter.type, value));
  }

  return parseScalar(parameter.type, rawValue);
}

export function formatOutput(value: unknown): string {
  if (Array.isArray(value)) {
    return value.map(formatOutput).join(', ');
  }

  if (value === null || value === undefined) {
    return '';
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return String(value);
}

function parseScalar(type: string, rawValue: string): unknown {
  switch (type) {
    case 'Boolean':
      return rawValue.toLowerCase() === 'true';
    case 'Integer':
      return Number.parseInt(rawValue, 10);
    case 'Float':
    case 'Double':
      return Number.parseFloat(rawValue);
    case 'Table':
      return JSON.parse(rawValue);
    default:
      return rawValue;
  }
}

async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  const text = await response.text();
  try {
    const error = JSON.parse(text) as { error?: string; detail?: string };
    throw new Error(`${error.error ?? response.statusText}: ${error.detail ?? text}`);
  } catch {
    throw new Error(`${response.status} ${response.statusText}: ${text}`);
  }
}

function joinUrl(hostUrl: string, path: string): string {
  return `${hostUrl.replace(/\/+$/, '')}/${path}`;
}
