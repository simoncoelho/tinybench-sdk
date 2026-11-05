import type { Edge, Node } from '@xyflow/react';

export type DriverDefinition = {
  driver: DriverDocument;
  connection: ConnectionDocument;
  commands: CommandDocument[];
};

export type DriverDocument = {
  id: string;
  class: string;
  displayName: string;
  manufacturer: string;
  model: string;
  version: string;
  description: string;
  requiredSoftware: string[];
  setupInstructions: string;
};

export type ConnectionDocument = {
  connectMethod: string;
  disconnectMethod: string;
  parameters: ParameterDocument[];
};

export type CommandDocument = {
  id: string;
  method: string;
  displayName: string;
  description: string;
  inputs: ParameterDocument[];
  outputs: ParameterDocument[];
};

export type ParameterDocument = {
  name: string;
  type: string;
  array: boolean;
  displayName: string;
  description: string;
  required: boolean;
  default: string | null;
};

export type CatalogDevice = {
  id: string;
  name: string;
  hostUrl: string;
  description: string;
};

export type AddedDevice = CatalogDevice & {
  definition: DriverDefinition;
  connected: boolean;
  connectionInputs: Record<string, string>;
};

export type StartNodeData = {
  kind: 'start';
};

export type EndNodeData = {
  kind: 'end';
  status: 'ready' | 'completed' | 'blocked';
};

export type RouteNodeData = {
  kind: 'route';
  routeKind: 'if-split' | 'if-join' | 'fanout-split' | 'fanout-join' | 'pipe-out';
  label: string;
  expression?: string;
  status?: 'ready' | 'completed' | 'blocked';
};

export type CommandNodeData = {
  kind: 'command';
  deviceId: string;
  deviceName: string;
  hostUrl: string;
  command: CommandDocument;
  inputs: Record<string, string>;
  status: 'ready' | 'running' | 'completed' | 'failed';
  outputs: Record<string, string>;
  error?: string;
};

export type VariableNodeData = {
  kind: 'variable';
  name: string;
  valueType: string;
  value: string;
};

export type WorkbenchNodeData = StartNodeData | EndNodeData | RouteNodeData | CommandNodeData | VariableNodeData;
export type WorkbenchNode = Node<WorkbenchNodeData>;

export type LogicEdgeMode = 'always' | 'if' | 'while';

export type WorkbenchEdgeData = {
  mode?: LogicEdgeMode;
  expression?: string;
  branch?: 'true' | 'false';
  label?: string;
  locked?: boolean;
  maxIterations?: number;
  status?: 'waiting' | 'running' | 'completed' | 'blocked';
};

export type WorkbenchEdge = Edge<WorkbenchEdgeData>;

export type RunSnapshot = {
  id: string;
  commandId: string;
  status: string;
  error: string | null;
  outputs: Record<string, unknown>;
};
