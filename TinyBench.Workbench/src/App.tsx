import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';
import {
  addEdge,
  Background,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type EdgeChange,
  type NodeProps,
  type NodeChange,
  type ReactFlowInstance,
} from '@xyflow/react';
import {
  connectDevice,
  disconnectDevice,
  formatOutput,
  getDefinition,
  getRun,
  parseInput,
  startRun,
} from './api';
import type {
  AddedDevice,
  CatalogDevice,
  CommandDocument,
  CommandNodeData,
  LogicEdgeMode,
  ParameterDocument,
  RouteNodeData,
  VariableNodeData,
  WorkbenchEdge,
  WorkbenchNode,
} from './types';

const catalogSeed: CatalogDevice[] = [
  {
    id: 'hig',
    name: 'HiG Centrifuge',
    hostUrl: 'http://localhost:5093',
    description: 'Door, rotor, spin, status.',
  },
  {
    id: 'plate-reader',
    name: 'Plate Reader',
    hostUrl: 'http://localhost:5094',
    description: 'Temperature, absorbance, status.',
  },
];

const startNode: WorkbenchNode = {
  id: 'start',
  type: 'start',
  position: { x: 320, y: 40 },
  data: { kind: 'start' },
  deletable: false,
};

const endNode: WorkbenchNode = {
  id: 'end',
  type: 'end',
  position: { x: 320, y: 560 },
  data: { kind: 'end', status: 'ready' },
  deletable: false,
};

const nodeTypes = {
  start: StartNode,
  end: EndNode,
  route: RouteNode,
  command: CommandNode,
  variable: VariableNode,
};

const variableTypes = ['Boolean', 'String', 'Integer', 'Float', 'String[]', 'Integer[]', 'Float[]', 'Boolean[]', 'Table'];
const completionControlKeys = new Set(['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape']);

const defaultScript = `TinyVariable status = new TinyVariable(HiG.ReadStatus.Outputs.doorOpen)
HiG.ReadStatus(out doorOpen = status)
TinyBench.If(status == false => { HiG.OpenDoor() }).Else(HiG.CloseDoor())`;

type CompletionItem = {
  label: string;
  detail: string;
  insertText: string;
};

type CompletionState = {
  items: CompletionItem[];
  selectedIndex: number;
  replaceFrom: number;
  replaceTo: number;
  top: number;
  left: number;
};

export function App() {
  return (
    <ReactFlowProvider>
      <Workbench />
    </ReactFlowProvider>
  );
}

function Workbench() {
  const wrapperRef = useRef<HTMLDivElement | null>(null);
  const flowRef = useRef<ReactFlowInstance<WorkbenchNode, WorkbenchEdge> | null>(null);
  const scriptEditorRef = useRef<HTMLTextAreaElement | null>(null);
  const scriptHighlightRef = useRef<HTMLPreElement | null>(null);
  const scriptLineNumberRef = useRef<HTMLDivElement | null>(null);
  const [catalog, setCatalog] = useState(catalogSeed);
  const [devices, setDevices] = useState<AddedDevice[]>([]);
  const [nodes, setNodes, onNodesChange] = useNodesState<WorkbenchNode>([startNode, endNode]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<WorkbenchEdge>([]);
  const [message, setMessage] = useState('Add devices, drag commands, connect execution top-to-bottom and data left-to-right.');
  const [busy, setBusy] = useState(false);
  const [nodeSequence, setNodeSequence] = useState(1);
  const [selection, setSelection] = useState<{ type: 'node' | 'edge'; id: string } | null>(null);
  const [scriptText, setScriptText] = useState(defaultScript);
  const [scriptMessages, setScriptMessages] = useState<string[]>([]);
  const [scriptOk, setScriptOk] = useState(false);
  const [scriptPanelWidth, setScriptPanelWidth] = useState(390);
  const [scriptCursor, setScriptCursor] = useState({ line: 1, column: 1 });
  const [definitionCache, setDefinitionCache] = useState<Record<string, AddedDevice>>({});
  const [completion, setCompletion] = useState<CompletionState | null>(null);

  const addedDeviceIds = useMemo(() => new Set(devices.map((device) => device.id)), [devices]);

  useEffect(() => {
    let canceled = false;

    void Promise.all(
      catalog.map(async (catalogDevice) => {
        try {
          const definition = await getDefinition(catalogDevice.hostUrl);
          const added: AddedDevice = {
            ...catalogDevice,
            name: display(definition.driver.displayName, catalogDevice.name),
            definition,
            connected: false,
            connectionInputs: Object.fromEntries(
              definition.connection.parameters.map((parameter) => [parameter.name, normalizeDefault(parameter.default)]),
            ),
          };
          return added;
        } catch {
          return null;
        }
      }),
    ).then((loaded) => {
      if (canceled) {
        return;
      }

      setDefinitionCache(
        Object.fromEntries(
          loaded
            .filter((device): device is AddedDevice => device !== null)
            .map((device) => [device.id, device]),
        ),
      );
    });

    return () => {
      canceled = true;
    };
  }, [catalog]);

  const updateCatalogUrl = (id: string, hostUrl: string) => {
    setCatalog((current) => current.map((device) => (device.id === id ? { ...device, hostUrl } : device)));
  };

  const addDevice = async (catalogDevice: CatalogDevice) => {
    if (addedDeviceIds.has(catalogDevice.id)) {
      return;
    }

    await runBusy(async () => {
      const definition = await getDefinition(catalogDevice.hostUrl);
      const added: AddedDevice = {
        ...catalogDevice,
        name: display(definition.driver.displayName, catalogDevice.name),
        definition,
        connected: false,
        connectionInputs: Object.fromEntries(
          definition.connection.parameters.map((parameter) => [
            parameter.name,
            normalizeDefault(parameter.default),
          ]),
        ),
      };

      setDevices((current) => [...current, added]);
      setMessage(`Added ${added.name}.`);
    });
  };

  const applyScript = async () => {
    try {
      setBusy(true);
      setScriptOk(false);
      setScriptMessages(['checking script...']);
      const graph = await buildGraphFromScript(scriptText, devices, catalog, nodeSequence);
      setDevices(graph.devices);
      setNodes(graph.nodes);
      setEdges(graph.edges);
      setNodeSequence(graph.nextSequence);
      setSelection(null);
      setScriptOk(true);
      setScriptMessages([
        `ok: ${graph.nodes.length - 1} blocks`,
        `ok: ${graph.edges.length} connections`,
      ]);
      setMessage(`Script built ${graph.nodes.length - 1} blocks.`);
    } catch (error) {
      const detail = error instanceof Error ? error.message : String(error);
      setScriptMessages([`error: ${detail}`]);
      setMessage(`Script check failed: ${detail}`);
    } finally {
      setBusy(false);
    }
  };

  const updateScriptCursor = () => {
    const editor = scriptEditorRef.current;
    if (!editor) {
      return;
    }

    const beforeCursor = editor.value.slice(0, editor.selectionStart);
    const lines = beforeCursor.split('\n');
    setScriptCursor({
      line: lines.length,
      column: lines[lines.length - 1].length + 1,
    });
  };

  const refreshCompletion = (text = scriptText) => {
    const editor = scriptEditorRef.current;
    if (!editor) {
      setCompletion(null);
      return;
    }

    const next = getCompletionState(editor, text, devices, definitionCache);
    setCompletion(next);
  };

  const acceptCompletion = (item = completion?.items[completion.selectedIndex]) => {
    if (!completion || !item || !scriptEditorRef.current) {
      return;
    }

    const editor = scriptEditorRef.current;
    const currentText = editor.value;
    const next = `${currentText.slice(0, completion.replaceFrom)}${item.insertText}${currentText.slice(completion.replaceTo)}`;
    const nextCursor = completion.replaceFrom + item.insertText.length;
    setScriptText(next);
    setCompletion(null);
    window.requestAnimationFrame(() => {
      editor.selectionStart = nextCursor;
      editor.selectionEnd = nextCursor;
      updateScriptCursor();
    });
  };

  const handleScriptKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (completion) {
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        setCompletion((current) =>
          current ? { ...current, selectedIndex: (current.selectedIndex + 1) % current.items.length } : current,
        );
        return;
      }

      if (event.key === 'ArrowUp') {
        event.preventDefault();
        setCompletion((current) =>
          current ? { ...current, selectedIndex: (current.selectedIndex - 1 + current.items.length) % current.items.length } : current,
        );
        return;
      }

      if (event.key === 'Enter' || event.key === 'Tab') {
        event.preventDefault();
        acceptCompletion();
        return;
      }

      if (event.key === 'Escape') {
        event.preventDefault();
        setCompletion(null);
        return;
      }
    }

    if (event.key === 'Tab') {
      event.preventDefault();
      const editor = event.currentTarget;
      const start = editor.selectionStart;
      const end = editor.selectionEnd;
      const next = `${editor.value.slice(0, start)}    ${editor.value.slice(end)}`;
      setScriptText(next);
      window.requestAnimationFrame(() => {
        editor.selectionStart = start + 4;
        editor.selectionEnd = start + 4;
        updateScriptCursor();
        refreshCompletion(next);
      });
    }

    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      void applyScript();
    }
  };

  const handleScriptScroll = () => {
    const editor = scriptEditorRef.current;
    if (!editor) {
      return;
    }

    if (scriptHighlightRef.current) {
      scriptHighlightRef.current.scrollTop = editor.scrollTop;
      scriptHighlightRef.current.scrollLeft = editor.scrollLeft;
    }

    if (scriptLineNumberRef.current) {
      scriptLineNumberRef.current.scrollTop = editor.scrollTop;
    }
  };

  const formatScript = () => {
    setScriptText((current) => formatWorkflowScript(current));
  };

  const lineNumbers = useMemo(
    () => Array.from({ length: Math.max(1, scriptText.split('\n').length) }, (_, index) => index + 1),
    [scriptText],
  );

  const beginScriptResize = (event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = scriptPanelWidth;

    const onMove = (moveEvent: PointerEvent) => {
      const nextWidth = Math.min(680, Math.max(280, startWidth + startX - moveEvent.clientX));
      setScriptPanelWidth(nextWidth);
    };

    const onUp = () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };

    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
  };

  const connectAddedDevice = async (device: AddedDevice) => {
    await runBusy(async () => {
      const parameters = buildValues(device.definition.connection.parameters, device.connectionInputs);
      await connectDevice(device.hostUrl, parameters);
      setDevices((current) =>
        current.map((candidate) =>
          candidate.id === device.id ? { ...candidate, connected: true } : candidate,
        ),
      );
      setMessage(`${device.name} connected.`);
    });
  };

  const disconnectAddedDevice = async (device: AddedDevice) => {
    await runBusy(async () => {
      await disconnectDevice(device.hostUrl);
      setDevices((current) =>
        current.map((candidate) =>
          candidate.id === device.id ? { ...candidate, connected: false } : candidate,
        ),
      );
      setMessage(`${device.name} disconnected.`);
    });
  };

  const updateConnectionInput = (deviceId: string, name: string, value: string) => {
    setDevices((current) =>
      current.map((device) =>
        device.id === deviceId
          ? { ...device, connectionInputs: { ...device.connectionInputs, [name]: value } }
          : device,
      ),
    );
  };

  const onDragStart = (
    event: React.DragEvent,
    payload: { type: 'command'; deviceId: string; commandId: string } | { type: 'variable' },
  ) => {
    event.dataTransfer.setData('application/tinybench-node', JSON.stringify(payload));
    event.dataTransfer.effectAllowed = 'move';
  };

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault();
      const raw = event.dataTransfer.getData('application/tinybench-node');
      if (!raw || !wrapperRef.current || !flowRef.current) {
        return;
      }

      const payload = JSON.parse(raw) as
        | { type: 'command'; deviceId: string; commandId: string }
        | { type: 'variable' };
      const position = flowRef.current.screenToFlowPosition({ x: event.clientX, y: event.clientY });
      const nextSequence = nodeSequence + 1;
      setNodeSequence(nextSequence);

      if (payload.type === 'variable') {
        setNodes((current) => [
          ...current,
          {
            id: `variable:${nextSequence}`,
            type: 'variable',
            position,
            data: {
              kind: 'variable',
              name: `var${nextSequence}`,
              valueType: 'Boolean',
              value: 'false',
            },
          },
        ]);
        return;
      }

      const device = devices.find((candidate) => candidate.id === payload.deviceId);
      const command = device?.definition.commands.find((candidate) => candidate.id === payload.commandId);
      if (!device || !command) {
        return;
      }

      setNodes((current) => [
        ...current,
        {
          id: `command:${nextSequence}`,
          type: 'command',
          position,
          data: {
            kind: 'command',
            deviceId: device.id,
            deviceName: device.name,
            hostUrl: device.hostUrl,
            command,
            inputs: Object.fromEntries(command.inputs.map((input) => [input.name, normalizeDefault(input.default)])),
            status: 'ready',
            outputs: {},
          },
        },
      ]);
      setEdges((current) => {
        const inserted = insertCommandIntoExecutionPath(current, nodes, `command:${nextSequence}`, position);
        if (inserted) {
          return inserted;
        }

        const source = lastExecutionSource(nodes, current);
        return addEdge(createExecutionEdge(source, `command:${nextSequence}`), current);
      });
    },
    [devices, edges, nodeSequence, nodes, setEdges, setNodes],
  );

  const isValidConnection = useCallback(
    (connection: Connection | Edge) => validateConnection(connection, nodes, edges),
    [edges, nodes],
  );

  const handleNodesChange = useCallback(
    (changes: NodeChange<WorkbenchNode>[]) => {
      const filtered = changes.filter((change) => {
        if (change.type !== 'remove') {
          return true;
        }

        const node = nodes.find((candidate) => candidate.id === change.id);
        return node?.data.kind !== 'route' && change.id !== 'start' && change.id !== 'end';
      });
      onNodesChange(filtered);
    },
    [nodes, onNodesChange],
  );

  const handleEdgesChange = useCallback(
    (changes: EdgeChange<WorkbenchEdge>[]) => {
      const filtered = changes.filter((change) => {
        if (change.type !== 'remove') {
          return true;
        }

        const edge = edges.find((candidate) => candidate.id === change.id);
        return !edge?.data?.locked;
      });
      onEdgesChange(filtered);
    },
    [edges, onEdgesChange],
  );

  const onConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target || !isValidConnection(connection)) {
        setMessage('Invalid connection. Execution uses bottom-to-top; data uses output-to-input with matching types.');
        return;
      }

      const sourceKind = handleKind(connection.sourceHandle);
      const normalizedConnection = normalizeVariableTargetConnection(connection, nodes);
      let edge: WorkbenchEdge = {
        ...normalizedConnection,
        id: `${normalizedConnection.source}:${normalizedConnection.sourceHandle}->${normalizedConnection.target}:${normalizedConnection.targetHandle}`,
        type: sourceKind === 'exec-out' ? 'smoothstep' : 'default',
        animated: sourceKind === 'exec-out',
        data: sourceKind === 'exec-out' ? { mode: 'always', expression: defaultBooleanExpression(nodes), maxIterations: 10 } : undefined,
        markerEnd: { type: MarkerType.ArrowClosed },
      };
      edge = applyEdgePresentation(edge);

      if (sourceKind === 'data-out' && edge.target) {
        const targetNode = nodes.find((node) => node.id === edge.target);
        const sourceType = handleType(edge.sourceHandle);
        if (targetNode?.data.kind === 'variable' && sourceType) {
          updateVariable(targetNode.id, { valueType: sourceType });
        }
      }

      setEdges((current) => addEdge(edge, current));
    },
    [isValidConnection, nodes, setEdges],
  );

  const updateCommandInput = (nodeId: string, inputName: string, value: string) => {
    setNodes((current) =>
      current.map((node) =>
        node.id === nodeId && node.data.kind === 'command'
          ? { ...node, data: { ...node.data, inputs: { ...node.data.inputs, [inputName]: value } } }
          : node,
      ),
    );
  };

  const updateVariable = (nodeId: string, patch: Partial<VariableNodeData>) => {
    if (patch.name !== undefined) {
      const nextName = patch.name;
      const currentNode = nodes.find((node) => node.id === nodeId && node.data.kind === 'variable');
      const previousName = currentNode?.data.kind === 'variable' ? currentNode.data.name : '';
      if (previousName && nextName && previousName !== nextName) {
        setEdges((current) =>
          current.map((edge) =>
            handleKind(edge.sourceHandle) === 'exec-out'
              ? applyEdgePresentation({
                  ...edge,
                  data: {
                    ...edge.data,
                    expression: replaceVariableInExpression(edge.data?.expression ?? '', previousName, nextName),
                  },
                })
              : edge,
          ),
        );
      }
    }

    setNodes((current) =>
      current.map((node) =>
        node.id === nodeId && node.data.kind === 'variable'
          ? { ...node, data: { ...node.data, ...patch } }
          : node,
      ),
    );
  };

  const updateEdgeLogic = (edgeId: string, patch: { mode?: LogicEdgeMode; expression?: string; branch?: 'true' | 'false'; maxIterations?: number }) => {
    setEdges((current) =>
      current.map((edge) =>
        edge.id === edgeId ? applyEdgePresentation({
          ...edge,
          animated: handleKind(edge.sourceHandle) === 'exec-out',
          data: normalizeLogicPatch(edge, patch, nodes),
        }) : edge,
      ),
    );
  };

  const deleteNode = (nodeId: string) => {
    if (nodeId === 'start' || nodeId === 'end') {
      return;
    }

    setNodes((current) => current.filter((node) => node.id !== nodeId));
    setEdges((current) => removeNodeFromEdges(nodeId, current));
    setSelection(null);
  };

  const deleteEdge = (edgeId: string) => {
    setEdges((current) => current.filter((edge) => edge.id !== edgeId));
    setSelection(null);
  };

  const insertCommandOnEdge = (edgeId: string, deviceId: string, commandId: string) => {
    const device = devices.find((candidate) => candidate.id === deviceId);
    const command = device?.definition.commands.find((candidate) => candidate.id === commandId);
    const edge = edges.find((candidate) => candidate.id === edgeId);
    if (!device || !command || !edge?.target) {
      return;
    }

    const nextSequence = nodeSequence + 1;
    setNodeSequence(nextSequence);
    const sourceNode = nodes.find((node) => node.id === edge.source);
    const targetNode = nodes.find((node) => node.id === edge.target);
    const position = {
      x: ((sourceNode?.position.x ?? 320) + (targetNode?.position.x ?? 520)) / 2,
      y: ((sourceNode?.position.y ?? 220) + (targetNode?.position.y ?? 360)) / 2,
    };
    const nodeId = `command:${nextSequence}`;

    setNodes((current) => [
      ...current,
      {
        id: nodeId,
        type: 'command',
        position,
        data: {
          kind: 'command',
          deviceId: device.id,
          deviceName: device.name,
          hostUrl: device.hostUrl,
          command,
          inputs: Object.fromEntries(command.inputs.map((input) => [input.name, normalizeDefault(input.default)])),
          status: 'ready',
          outputs: {},
        },
      },
    ]);
    setEdges((current) => insertCommandAtEdge(current, edgeId, nodeId) ?? current);
    setSelection({ type: 'node', id: nodeId });
  };

  const insertCommandOnRoutePath = (routeNodeId: string, branch: 'TRUE' | 'FALSE', deviceId: string, commandId: string) => {
    const edge = edges.find((candidate) => candidate.source === routeNodeId && candidate.data?.label === branch);
    if (edge) {
      insertCommandOnEdge(edge.id, deviceId, commandId);
    }
  };

  nodeActions = {
    updateCommandInput,
    updateVariable,
    edges,
  };

  const runWorkflow = async () => {
    await runBusy(async () => {
      if (!edges.some((edge) => edge.source === 'start' && edge.sourceHandle === 'exec-out')) {
        setMessage('Connect Start to at least one command using execution ports.');
        return;
      }

      const graphIssue = validateExecutionGraph(nodes, edges);
      if (graphIssue) {
        setMessage(graphIssue);
        return;
      }

      let currentNodes = nodes.map((node) =>
        node.data.kind === 'command'
          ? { ...node, data: { ...node.data, status: 'ready' as const, outputs: {}, error: undefined } }
          : node.data.kind === 'end'
            ? { ...node, data: { ...node.data, status: 'ready' as const } }
            : node.data.kind === 'route'
              ? { ...node, data: { ...node.data, status: 'ready' as const } }
          : node,
      );
      let runEdges = edges.map((edge) => applyEdgePresentation({
        ...edge,
        data: { ...edge.data, status: 'waiting' as const },
      }));
      setNodes(currentNodes);
      setEdges(runEdges);

      const variables = new Map<string, string>();
      syncVariables(currentNodes, variables);
      const activeEdges = new Set<string>();
      const executedCommands = new Set<string>();
      const queuedEdges = new Set<string>();
      const queue = enqueueExecutionEdges('start', runEdges, variables, activeEdges, queuedEdges);
      let safety = 0;

      while (queue.length > 0 && safety++ < 200) {
        const nextEdge = queue.shift();
        if (!nextEdge?.target) {
          continue;
        }

        runEdges = markEdge(runEdges, nextEdge.id, 'running');
        setEdges(runEdges);

        if (nextEdge.target === 'end') {
          runEdges = markEdge(runEdges, nextEdge.id, 'completed');
          setEdges(runEdges);
          continue;
        }

        const targetNode = currentNodes.find((node) => node.id === nextEdge.target);
        if (targetNode?.data.kind === 'route') {
          runEdges = markEdge(runEdges, nextEdge.id, 'completed');
          const isJoin = targetNode.data.routeKind.endsWith('join');
          const activeIncoming = runEdges.filter((edge) => edge.target === targetNode.id && activeEdges.has(edge.id));
          const joinReady = !isJoin || activeIncoming.every((edge) => edge.data?.status === 'completed');
          if (!joinReady) {
            setEdges(runEdges);
            continue;
          }

          currentNodes = markRoute(currentNodes, targetNode.id, { status: 'completed' });
          setNodes(currentNodes);
          setEdges(runEdges);
          queue.push(...enqueueExecutionEdges(targetNode.id, runEdges, variables, activeEdges, queuedEdges));
          continue;
        }

        const commandNodeId = nextEdge.target;
        if (executedCommands.has(commandNodeId)) {
          runEdges = markEdge(runEdges, nextEdge.id, 'completed');
          setEdges(runEdges);
          queue.push(...enqueueExecutionEdges(commandNodeId, runEdges, variables, activeEdges, queuedEdges));
          continue;
        }

        currentNodes = markCommand(currentNodes, commandNodeId, { status: 'running', error: undefined });
        setNodes(currentNodes);

        const commandNode = currentNodes.find((node) => node.id === commandNodeId);
        if (!commandNode || !isCommandNode(commandNode)) {
          runEdges = markEdge(runEdges, nextEdge.id, 'blocked');
          setEdges(runEdges);
          continue;
        }

        const inputs = buildCommandInputs(commandNode, currentNodes, edges);
        let run = await startRun(commandNode.data.hostUrl, commandNode.data.command.id, inputs);
        while (['Queued', 'Running', 'CancelRequested'].includes(run.status)) {
          await delay(200);
          run = await getRun(commandNode.data.hostUrl, run.id);
        }

        const outputs = Object.fromEntries(
          Object.entries(run.outputs ?? {}).map(([key, value]) => [key, formatOutput(value)]),
        );

        if (run.status !== 'Completed') {
          currentNodes = markCommand(currentNodes, commandNodeId, {
            status: 'failed',
            outputs,
            error: run.error ?? run.status,
          });
          setNodes(currentNodes);
          runEdges = markEdge(runEdges, nextEdge.id, 'blocked');
          setEdges(runEdges);
          setMessage(`${commandNode.data.command.id} failed: ${run.error ?? run.status}`);
          return;
        }

        executedCommands.add(commandNodeId);
        currentNodes = markCommand(currentNodes, commandNodeId, {
          status: 'completed',
          outputs,
          error: undefined,
        });
        runEdges = markEdge(runEdges, nextEdge.id, 'completed');
        currentNodes = propagateDataEdges(commandNodeId, outputs, currentNodes, runEdges);
        syncVariables(currentNodes, variables);
        setNodes(currentNodes);
        setEdges(runEdges);
        queue.push(...enqueueExecutionEdges(commandNodeId, runEdges, variables, activeEdges, queuedEdges));
      }

      if (safety >= 200) {
        setMessage('Workflow stopped at safety limit.');
        return;
      }

      const activeEndEdges = runEdges.filter((edge) => edge.target === 'end' && activeEdges.has(edge.id));
      const endComplete = activeEndEdges.length > 0 && activeEndEdges.every((edge) => edge.data?.status === 'completed');
      currentNodes = currentNodes.map((node) =>
        node.data.kind === 'end'
          ? { ...node, data: { ...node.data, status: endComplete ? 'completed' as const : 'blocked' as const } }
          : node,
      );
      setNodes(currentNodes);
      setMessage(endComplete ? 'Workflow completed at End.' : 'Workflow stopped before all active paths reached End.');
    });
  };

  async function runBusy(action: () => Promise<void>) {
    try {
      setBusy(true);
      await action();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="app-shell" style={{ '--script-panel-width': `${scriptPanelWidth}px` } as CSSProperties}>
      <aside className="sidebar">
        <div className="topline">
          <strong>TinyBench</strong>
          <span>{message}</span>
        </div>

        <details open className="side-section">
          <summary>Available devices</summary>
          <div className="rows">
            {catalog.map((device) => (
              <div key={device.id} className="row">
                <div className="row-main">
                  <strong>{device.name}</strong>
                  <span>{device.description}</span>
                </div>
                <input
                  aria-label={`${device.name} host URL`}
                  value={device.hostUrl}
                  onChange={(event) => updateCatalogUrl(device.id, event.target.value)}
                />
                <button type="button" disabled={busy || addedDeviceIds.has(device.id)} onClick={() => void addDevice(device)}>
                  {addedDeviceIds.has(device.id) ? 'Added' : 'Add'}
                </button>
              </div>
            ))}
          </div>
        </details>

        <details open className="side-section">
          <summary>System devices</summary>
          {devices.length === 0 ? (
            <p className="empty-line">No devices added.</p>
          ) : (
            <div className="rows">
              {devices.map((device) => (
                <div key={device.id} className="device-block">
                  <div className="device-title">
                    <strong>{device.name}</strong>
                    <span className={device.connected ? 'status connected' : 'status'}>
                      {device.connected ? 'connected' : 'offline'}
                    </span>
                  </div>
                  <div className="mini-grid">
                    {device.definition.connection.parameters.map((parameter) => (
                      <label key={parameter.name}>
                        <span>{label(parameter)}</span>
                        <input
                          value={device.connectionInputs[parameter.name] ?? ''}
                          placeholder={placeholder(parameter)}
                          onChange={(event) => updateConnectionInput(device.id, parameter.name, event.target.value)}
                        />
                      </label>
                    ))}
                  </div>
                  <div className="button-row">
                    <button type="button" disabled={busy} onClick={() => void connectAddedDevice(device)}>
                      Connect
                    </button>
                    <button type="button" className="secondary" disabled={busy} onClick={() => void disconnectAddedDevice(device)}>
                      Disconnect
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </details>

        <details open className="side-section">
          <summary>Nodes</summary>
          <button
            type="button"
            className="drag-row variable"
            draggable
            onDragStart={(event) => onDragStart(event, { type: 'variable' })}
          >
            Variable
          </button>

          {devices.map((device) => (
            <details key={device.id} open className="command-section">
              <summary>{device.name}</summary>
              <div className="command-list">
                {device.definition.commands.map((command) => (
                  <button
                    type="button"
                    key={command.id}
                    className="drag-row command"
                    draggable
                    onDragStart={(event) => onDragStart(event, { type: 'command', deviceId: device.id, commandId: command.id })}
                  >
                    <span>{display(command.displayName, command.id)}</span>
                    <em>{inputSummary(command)}</em>
                  </button>
                ))}
              </div>
            </details>
          ))}
        </details>
      </aside>

      <section className="workspace">
        <div className="toolbar">
          <button type="button" disabled={busy} onClick={() => void runWorkflow()}>
            Run
          </button>
          <button type="button" className="secondary" disabled={busy} onClick={() => { setNodes([startNode, endNode]); setEdges([]); }}>
            Clear flow
          </button>
          <span>Execution: bottom to top. Data: right to left.</span>
        </div>

        <div className="flow-wrap" ref={wrapperRef}>
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            isValidConnection={isValidConnection}
            onNodesChange={handleNodesChange}
            onEdgesChange={handleEdgesChange}
            onConnect={onConnect}
            onNodeClick={(_, node) => setSelection({ type: 'node', id: node.id })}
            onEdgeClick={(_, edge) => setSelection({ type: 'edge', id: edge.id })}
            onPaneClick={() => setSelection(null)}
            onInit={(instance) => {
              flowRef.current = instance;
            }}
            onDrop={onDrop}
            onDragOver={(event) => {
              event.preventDefault();
              event.dataTransfer.dropEffect = 'move';
            }}
            fitView
          >
            <Background gap={20} />
            <MiniMap pannable zoomable />
            <Controls />
          </ReactFlow>
        </div>

        <InspectorPanel
          selection={selection}
          nodes={nodes}
          edges={edges}
          onUpdateVariable={updateVariable}
          onUpdateCommandInput={updateCommandInput}
          onUpdateEdgeLogic={updateEdgeLogic}
          onDeleteNode={deleteNode}
          onDeleteEdge={deleteEdge}
          onInsertCommandOnEdge={insertCommandOnEdge}
          onInsertCommandOnRoutePath={insertCommandOnRoutePath}
          devices={devices}
        />
      </section>

      <aside className="script-panel">
        <div className="resize-handle" onPointerDown={beginScriptResize} />
        <div className="script-title">
          <strong>Script</strong>
          <div className="script-actions">
            <button type="button" className="secondary" onClick={formatScript}>
              Format
            </button>
            <button type="button" className="secondary" onClick={() => setScriptText(defaultScript)}>
              Reset
            </button>
            <button type="button" className="secondary" onClick={() => setScriptText('')}>
              Clear
            </button>
            <button type="button" disabled={busy} onClick={() => void applyScript()}>
              Check + build
            </button>
          </div>
        </div>
        <div className="editor-shell">
          <div className="line-numbers" ref={scriptLineNumberRef} aria-hidden="true">
            {lineNumbers.map((line) => (
              <div key={line}>{line}</div>
            ))}
          </div>
          <div className="editor-stack">
            <pre className="script-highlight" ref={scriptHighlightRef} aria-hidden="true">
              {scriptText.split('\n').map((line, index) => (
                <span className="code-line" key={`${index}-${line}`}>
                  {highlightScriptLine(line)}
                  {'\n'}
                </span>
              ))}
            </pre>
            <textarea
              ref={scriptEditorRef}
              value={scriptText}
              spellCheck={false}
          onChange={(event) => {
              setScriptText(event.target.value);
              const nextText = event.target.value;
              window.requestAnimationFrame(() => {
                updateScriptCursor();
                refreshCompletion(nextText);
              });
            }}
            onClick={(event) => {
              updateScriptCursor();
              refreshCompletion(event.currentTarget.value);
            }}
            onKeyDown={handleScriptKeyDown}
            onKeyUp={(event) => {
              updateScriptCursor();
              if (!completionControlKeys.has(event.key)) {
                refreshCompletion(event.currentTarget.value);
              }
            }}
            onScroll={handleScriptScroll}
            onSelect={(event) => {
              updateScriptCursor();
              if (event.currentTarget.selectionStart !== event.currentTarget.selectionEnd) {
                setCompletion(null);
              }
            }}
          />
            {completion && (
              <div className="completion-menu" style={{ top: completion.top, left: completion.left }}>
                {completion.items.map((item, index) => (
                  <button
                    type="button"
                    key={item.label}
                    className={index === completion.selectedIndex ? 'selected' : ''}
                    onMouseDown={(event) => {
                      event.preventDefault();
                      acceptCompletion(item);
                    }}
                  >
                    <span>{item.label}</span>
                    <em>{item.detail}</em>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
        <div className="editor-footer">
          <span>Ln {scriptCursor.line}, Col {scriptCursor.column}</span>
          <span>Tab indents · Ctrl+Enter builds</span>
        </div>
        <div className={scriptOk ? 'script-status ok' : 'script-status'}>
          {scriptMessages.length === 0
            ? 'Write workflow script, then check + build.'
            : scriptMessages.map((line) => <div key={line}>{line}</div>)}
        </div>
      </aside>
    </main>
  );
}

function StartNode() {
  return (
    <section className="graph-node start-node">
      <strong>Start</strong>
      <span>execution</span>
      <Handle type="source" position={Position.Bottom} id="exec-out" className="exec-handle" />
    </section>
  );
}

function EndNode({ id, data }: NodeProps<WorkbenchNode>) {
  const app = useWorkbenchNodeActions();
  if (data.kind !== 'end') {
    return null;
  }

  const incoming = app.edges.filter((edge) => edge.target === id && edge.targetHandle === 'exec-in');
  const activeIncoming = incoming.filter((edge) => edge.data?.status && edge.data.status !== 'waiting');
  const displayIncoming = activeIncoming.length > 0 ? activeIncoming : incoming;
  const completed = displayIncoming.filter((edge) => edge.data?.status === 'completed');

  return (
    <section className={`graph-node end-node ${data.status}`}>
      <Handle type="target" position={Position.Top} id="exec-in" className="exec-handle" />
      <strong>End</strong>
      <span>{completed.length}/{displayIncoming.length} {activeIncoming.length > 0 ? 'active ' : ''}paths green</span>
    </section>
  );
}

function RouteNode({ id, data }: NodeProps<WorkbenchNode>) {
  if (data.kind !== 'route') {
    return null;
  }

  const hasInput = true;
  const hasOutput = data.routeKind !== 'pipe-out';

  return (
    <section className={`route-node ${data.routeKind} ${data.status ?? 'ready'}`}>
      {hasInput && <Handle type="target" position={Position.Top} id="exec-in" className="exec-handle" />}
      <strong>{data.label}</strong>
      {data.expression && <span>{data.expression}</span>}
      {hasOutput && <Handle type="source" position={Position.Bottom} id="exec-out" className="exec-handle" />}
    </section>
  );
}

function InspectorPanel({
  selection,
  nodes,
  edges,
  onUpdateVariable,
  onUpdateCommandInput,
  onUpdateEdgeLogic,
  onDeleteNode,
  onDeleteEdge,
  onInsertCommandOnEdge,
  onInsertCommandOnRoutePath,
  devices,
}: {
  selection: { type: 'node' | 'edge'; id: string } | null;
  nodes: WorkbenchNode[];
  edges: WorkbenchEdge[];
  devices: AddedDevice[];
  onUpdateVariable: (nodeId: string, patch: Partial<VariableNodeData>) => void;
  onUpdateCommandInput: (nodeId: string, inputName: string, value: string) => void;
  onUpdateEdgeLogic: (edgeId: string, patch: { mode?: LogicEdgeMode; expression?: string; branch?: 'true' | 'false'; maxIterations?: number }) => void;
  onDeleteNode: (nodeId: string) => void;
  onDeleteEdge: (edgeId: string) => void;
  onInsertCommandOnEdge: (edgeId: string, deviceId: string, commandId: string) => void;
  onInsertCommandOnRoutePath: (routeNodeId: string, branch: 'TRUE' | 'FALSE', deviceId: string, commandId: string) => void;
}) {
  const selectedNode = selection?.type === 'node' ? nodes.find((node) => node.id === selection.id) : undefined;
  const selectedEdge = selection?.type === 'edge' ? edges.find((edge) => edge.id === selection.id) : undefined;

  if (!selection) {
    return null;
  }

  return (
    <aside className="inspector">
      <div className="inspector-title">Inspector</div>

      {selectedNode?.data.kind === 'start' && (
        <section className="inspector-section">
          <h2>Start</h2>
          <p>Execution begins here. Connect its bottom port to the first command.</p>
        </section>
      )}

      {selectedNode?.data.kind === 'end' && (
        <section className="inspector-section">
          <h2>End</h2>
          <p>Fan-in point. Workflow completes only when every active incoming path is green.</p>
        </section>
      )}

      {selectedNode?.data.kind === 'route' && (
        <section className="inspector-section">
          <h2>{selectedNode.data.routeKind === 'if-split' ? 'If path' : selectedNode.data.routeKind === 'if-join' ? 'End if' : selectedNode.data.label}</h2>
          <p>{selectedNode.data.routeKind.replace('-', ' ')}</p>
          {selectedNode.data.routeKind === 'if-split' && (
            <div className="path-insert-groups">
              {(['TRUE', 'FALSE'] as const).map((branch) => (
                <section key={branch}>
                  <span>{branch} path</span>
                  <div className="path-insert-list">
                    {devices.flatMap((device) =>
                      device.definition.commands.map((command) => (
                        <button
                          type="button"
                          key={`${branch}:${device.id}:${command.id}`}
                          onClick={() => onInsertCommandOnRoutePath(selectedNode.id, branch, device.id, command.id)}
                        >
                          {device.name} / {command.id}
                        </button>
                      )),
                    )}
                  </div>
                </section>
              ))}
            </div>
          )}
        </section>
      )}

      {selectedNode?.data.kind === 'command' && (
        <section className="inspector-section">
          <h2>{display(selectedNode.data.command.displayName, selectedNode.data.command.id)}</h2>
          <p>{selectedNode.data.deviceName}</p>
          <button type="button" className="danger" onClick={() => onDeleteNode(selectedNode.id)}>
            Delete block
          </button>
          {selectedNode.data.command.inputs.map((input) => {
            const linked = edges.some((edge) => edge.target === selectedNode.id && edge.targetHandle === dataInHandle(input));
            return (
              <label key={input.name}>
                <span>{input.name} · {parameterType(input)}</span>
                <input
                  value={selectedNode.data.kind === 'command' ? selectedNode.data.inputs[input.name] ?? '' : ''}
                  disabled={linked}
                  placeholder={linked ? 'linked from data edge' : parameterType(input)}
                  onChange={(event) => onUpdateCommandInput(selectedNode.id, input.name, event.target.value)}
                />
              </label>
            );
          })}
          {Object.keys(selectedNode.data.outputs).length > 0 && (
            <div className="kv-list">
              {Object.entries(selectedNode.data.outputs).map(([key, value]) => (
                <div key={key}><span>{key}</span><code>{value}</code></div>
              ))}
            </div>
          )}
        </section>
      )}

      {selectedNode?.data.kind === 'variable' && (
        <section className="inspector-section">
          <h2>Variable</h2>
          <button type="button" className="danger" onClick={() => onDeleteNode(selectedNode.id)}>
            Delete block
          </button>
          <label>
            <span>Name</span>
            <input value={selectedNode.data.name} onChange={(event) => onUpdateVariable(selectedNode.id, { name: event.target.value })} />
          </label>
          <label>
            <span>Type</span>
            <select value={selectedNode.data.valueType} onChange={(event) => onUpdateVariable(selectedNode.id, { valueType: event.target.value })}>
              {variableTypes.map((type) => <option key={type} value={type}>{type}</option>)}
            </select>
          </label>
          <label>
            <span>Default / current value</span>
            <input value={selectedNode.data.value} onChange={(event) => onUpdateVariable(selectedNode.id, { value: event.target.value })} />
          </label>
        </section>
      )}

      {selectedEdge && handleKind(selectedEdge.sourceHandle) === 'exec-out' && (
        <section className="inspector-section">
          <h2>Execution connection</h2>
          {selectedEdge.data?.locked ? (
            <>
              <p>Required if-path scaffold. Add or remove command blocks inside the TRUE/FALSE paths instead.</p>
              {(selectedEdge.data.label === 'TRUE' || selectedEdge.data.label === 'FALSE') && (
                <div className="path-insert-list">
                  {devices.flatMap((device) =>
                    device.definition.commands.map((command) => (
                      <button
                        type="button"
                        key={`${device.id}:${command.id}`}
                        onClick={() => onInsertCommandOnEdge(selectedEdge.id, device.id, command.id)}
                      >
                        {device.name} / {command.id}
                      </button>
                    )),
                  )}
                </div>
              )}
            </>
          ) : (
            <>
              <button type="button" className="danger" onClick={() => onDeleteEdge(selectedEdge.id)}>
                Disconnect connection
              </button>
              <div className="edge-mode-tools">
                <button type="button" onClick={() => onUpdateEdgeLogic(selectedEdge.id, { mode: 'always' })}>Always</button>
                <button type="button" onClick={() => onUpdateEdgeLogic(selectedEdge.id, { mode: 'if', branch: 'true' })}>If true</button>
                <button type="button" onClick={() => onUpdateEdgeLogic(selectedEdge.id, { mode: 'if', branch: 'false' })}>If false</button>
                <button type="button" onClick={() => onUpdateEdgeLogic(selectedEdge.id, { mode: 'while' })}>While</button>
              </div>
              <label>
                <span>Mode</span>
                <select
                  value={selectedEdge.data?.mode ?? 'always'}
                  onChange={(event) => onUpdateEdgeLogic(selectedEdge.id, { mode: event.target.value as LogicEdgeMode })}
                >
                  <option value="always">always</option>
                  <option value="if">if</option>
                  <option value="while">while</option>
                </select>
              </label>
              {selectedEdge.data?.mode === 'if' && (
                <label>
                  <span>Branch</span>
                  <select
                    value={selectedEdge.data?.branch ?? branchFromExpression(selectedEdge.data?.expression ?? '') ?? 'true'}
                    onChange={(event) => onUpdateEdgeLogic(selectedEdge.id, { branch: event.target.value as 'true' | 'false' })}
                  >
                    <option value="true">true path</option>
                    <option value="false">false path</option>
                  </select>
                </label>
              )}
              {(selectedEdge.data?.mode === 'if' || selectedEdge.data?.mode === 'while') && (
                <label>
                  <span>Expression</span>
                  <input
                    value={selectedEdge.data?.expression ?? ''}
                    placeholder="status"
                    onChange={(event) => onUpdateEdgeLogic(selectedEdge.id, { expression: event.target.value })}
                  />
                </label>
              )}
              {selectedEdge.data?.mode === 'while' && (
                <label>
                  <span>Max iterations</span>
                  <input
                    type="number"
                    min="1"
                    max="100"
                    value={selectedEdge.data?.maxIterations ?? 10}
                    onChange={(event) => onUpdateEdgeLogic(selectedEdge.id, { maxIterations: Number(event.target.value) })}
                  />
                </label>
              )}
              <p>Expressions read variable names. Supported: `flag`, `!flag`, `x == 1`, `x != done`, `x &gt; 3`.</p>
            </>
          )}
        </section>
      )}

      {selectedEdge && handleKind(selectedEdge.sourceHandle) === 'data-out' && (
        <section className="inspector-section">
          <h2>Data connection</h2>
          <button type="button" className="danger" onClick={() => onDeleteEdge(selectedEdge.id)}>
            Disconnect connection
          </button>
          <p>{handleType(selectedEdge.sourceHandle)} value flows into {handleName(selectedEdge.targetHandle)}.</p>
        </section>
      )}
    </aside>
  );
}

function CommandNode({ id, data }: NodeProps<WorkbenchNode>) {
  const app = useWorkbenchNodeActions();
  if (data.kind !== 'command') {
    return null;
  }

  return (
    <section className={`graph-node command-node ${data.status}`}>
      <Handle type="target" position={Position.Top} id="exec-in" className="exec-handle" />
      <Handle type="source" position={Position.Bottom} id="exec-out" className="exec-handle" />

      <div className="exec-wrapper-label">complete {'->'} next</div>
      <div className="node-title">
        <strong>{display(data.command.displayName, data.command.id)}</strong>
        <span>{data.deviceName}</span>
      </div>

      {data.command.inputs.length > 0 && (
        <div className="ports inputs">
          {data.command.inputs.map((input) => {
            const linked = app.edges.some((edge) => edge.target === id && edge.targetHandle === dataInHandle(input));
            return (
              <label key={input.name} className="port-row">
                <Handle type="target" position={Position.Left} id={dataInHandle(input)} className="data-handle" />
                <span>{input.name}</span>
                <input
                  value={data.inputs[input.name] ?? ''}
                  disabled={linked}
                  placeholder={linked ? 'linked' : parameterType(input)}
                  onChange={(event) => app.updateCommandInput(id, input.name, event.target.value)}
                />
              </label>
            );
          })}
        </div>
      )}

      {data.command.outputs.length > 0 && (
        <div className="ports outputs">
          {data.command.outputs.map((output) => (
            <div key={output.name} className="port-row output-row">
              <span>{output.name}</span>
              <code>{data.outputs[output.name] ?? parameterType(output)}</code>
              <Handle type="source" position={Position.Right} id={dataOutHandle(output)} className="data-handle" />
            </div>
          ))}
        </div>
      )}

      <div className="node-footer">{data.status}</div>
      {data.error && <p className="node-error">{data.error}</p>}
    </section>
  );
}

function VariableNode({ id, data }: NodeProps<WorkbenchNode>) {
  const app = useWorkbenchNodeActions();
  if (data.kind !== 'variable') {
    return null;
  }

  const incoming = app.edges.some((edge) => edge.target === id && edge.targetHandle === variableInHandle(data.valueType));

  return (
    <section className="graph-node variable-node">
      <Handle type="target" position={Position.Left} id={variableInHandle(data.valueType)} className="data-handle" />
      <Handle type="source" position={Position.Right} id={variableOutHandle(data.valueType)} className="data-handle" />

      <div className="node-title">
        <strong>{data.name || 'Variable'}</strong>
        <span>{data.valueType}</span>
      </div>
      <label>
        <span>Name</span>
        <input
          value={data.name}
          aria-label="Variable name"
          onChange={(event) => app.updateVariable(id, { name: event.target.value })}
        />
      </label>
      <label>
        <span>{incoming ? 'Value from input' : 'Default value'}</span>
        <input
          value={data.value}
          disabled={incoming}
          placeholder={incoming ? 'linked' : data.valueType}
          onChange={(event) => app.updateVariable(id, { value: event.target.value })}
        />
      </label>
      <label>
        <span>Type</span>
        <select
          value={data.valueType}
          disabled={incoming || app.edges.some((edge) => edge.source === id)}
          onChange={(event) => app.updateVariable(id, { valueType: event.target.value })}
        >
          {variableTypes.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </select>
      </label>
    </section>
  );
}

let nodeActions: {
  updateCommandInput: (nodeId: string, inputName: string, value: string) => void;
  updateVariable: (nodeId: string, patch: Partial<VariableNodeData>) => void;
  edges: WorkbenchEdge[];
} = {
  updateCommandInput: () => undefined,
  updateVariable: () => undefined,
  edges: [],
};

function useWorkbenchNodeActions() {
  return nodeActions;
}

function validateConnection(connection: Connection | Edge, nodes: WorkbenchNode[], edges: WorkbenchEdge[]): boolean {
  if (!connection.source || !connection.target || connection.source === connection.target) {
    return false;
  }

  const sourceNode = nodes.find((node) => node.id === connection.source);
  const targetNode = nodes.find((node) => node.id === connection.target);
  if (!sourceNode || !targetNode) {
    return false;
  }

  const sourceKind = handleKind(connection.sourceHandle);
  const targetKind = handleKind(connection.targetHandle);
  if (sourceKind === 'exec-out' || targetKind === 'exec-in') {
    return sourceKind === 'exec-out' &&
      targetKind === 'exec-in' &&
      sourceNode.data.kind !== 'end' &&
      (targetNode.data.kind === 'command' || targetNode.data.kind === 'route' || targetNode.data.kind === 'end');
  }

  if (sourceKind !== 'data-out' || targetKind !== 'data-in') {
    return false;
  }

  const sourceType = handleType(connection.sourceHandle);
  if (targetNode.data.kind === 'variable') {
    const targetType = handleType(connection.targetHandle);
    const hasIncoming = edges.some((edge) => edge.target === targetNode.id && handleKind(edge.targetHandle) === 'data-in');
    return !hasIncoming || sourceType === targetType;
  }

  const targetType = handleType(connection.targetHandle);
  return sourceType === targetType;
}

function normalizeVariableTargetConnection(connection: Connection, nodes: WorkbenchNode[]): Connection {
  if (!connection.target || handleKind(connection.sourceHandle) !== 'data-out') {
    return connection;
  }

  const targetNode = nodes.find((node) => node.id === connection.target);
  const sourceType = handleType(connection.sourceHandle);
  if (targetNode?.data.kind !== 'variable' || !sourceType) {
    return connection;
  }

  return {
    ...connection,
    targetHandle: variableInHandle(sourceType),
  };
}

function buildCommandInputs(
  commandNode: WorkbenchNode & { data: CommandNodeData },
  nodes: WorkbenchNode[],
  edges: WorkbenchEdge[],
): Record<string, unknown> {
  const rawInputs = { ...commandNode.data.inputs };

  for (const edge of edges) {
    if (edge.target !== commandNode.id || handleKind(edge.targetHandle) !== 'data-in') {
      continue;
    }

    const inputName = handleName(edge.targetHandle);
    const sourceNode = nodes.find((node) => node.id === edge.source);
    if (sourceNode?.data.kind === 'variable') {
      rawInputs[inputName] = sourceNode.data.value;
    }
    if (sourceNode?.data.kind === 'command') {
      rawInputs[inputName] = sourceNode.data.outputs[handleName(edge.sourceHandle)] ?? '';
    }
  }

  const values: Record<string, unknown> = {};
  for (const parameter of commandNode.data.command.inputs) {
    const parsed = parseInput(parameter, rawInputs[parameter.name] ?? '');
    if (parsed !== undefined) {
      values[parameter.name] = parsed;
    }
  }

  return values;
}

function propagateDataEdges(
  sourceNodeId: string,
  outputs: Record<string, string>,
  nodes: WorkbenchNode[],
  edges: WorkbenchEdge[],
): WorkbenchNode[] {
  const updates = new Map<string, string>();

  for (const edge of edges) {
    if (edge.source !== sourceNodeId || handleKind(edge.sourceHandle) !== 'data-out') {
      continue;
    }

    const targetNode = nodes.find((node) => node.id === edge.target);
    if (targetNode?.data.kind === 'variable') {
      const outputName = handleName(edge.sourceHandle);
      updates.set(targetNode.id, outputs[outputName] ?? '');
    }
  }

  if (updates.size === 0) {
    return nodes;
  }

  return nodes.map((node) =>
    node.data.kind === 'variable' && updates.has(node.id)
      ? { ...node, data: { ...node.data, value: updates.get(node.id) ?? node.data.value } }
      : node,
  );
}

function executionOrder(nodes: WorkbenchNode[], edges: WorkbenchEdge[]): string[] {
  const order: string[] = [];
  const visited = new Set<string>();
  let current = 'start';

  while (true) {
    const next = edges.find((edge) => edge.source === current && edge.sourceHandle === 'exec-out');
    if (!next?.target || visited.has(next.target)) {
      break;
    }

    const targetNode = nodes.find((node) => node.id === next.target);
    if (!targetNode || targetNode.data.kind !== 'command') {
      break;
    }

    visited.add(next.target);
    order.push(next.target);
    current = next.target;
  }

  return order;
}

function validateExecutionGraph(nodes: WorkbenchNode[], edges: WorkbenchEdge[]): string | null {
  const endExists = nodes.some((node) => node.id === 'end' && node.data.kind === 'end');
  if (!endExists) {
    return 'Add an End block before running.';
  }

  const endIncoming = edges.filter((edge) => edge.target === 'end' && edge.targetHandle === 'exec-in');
  if (endIncoming.length === 0) {
    return 'Connect every execution path into End before running.';
  }

  for (const node of nodes) {
    if (node.data.kind !== 'start' && node.data.kind !== 'command' && node.data.kind !== 'route') {
      continue;
    }

    const outgoing = edges.filter((edge) => edge.source === node.id && edge.sourceHandle === 'exec-out');
    if (node.data.kind === 'command' && outgoing.length === 0) {
      return `${node.data.command.id} has no outgoing execution path. Connect it to End.`;
    }

    if (node.data.kind === 'command' && outgoing.length > 1) {
      return `${node.data.command.id} has multiple next paths. Add an explicit fan-out path node.`;
    }

    const missingEnd = outgoing.find((edge) => !edge.target || !canReachEnd(edge.target, edges));
    if (missingEnd) {
      const label = executionNodeLabel(node);
      return `${label} has an execution path that does not reach End.`;
    }

    if (outgoing.length <= 1) {
      continue;
    }

    if (node.data.kind !== 'route' || !node.data.routeKind.endsWith('split')) {
      return `${executionNodeLabel(node)} fans out. Use an explicit fan-out or if path node.`;
    }

    const joinKind = node.data.routeKind === 'if-split' ? 'if-join' : 'fanout-join';
    const branchWithoutJoin = outgoing.find((edge) => !edge.target || !canReachRouteJoin(edge.target, joinKind, nodes, edges));
    if (branchWithoutJoin) {
      return `${node.data.label} splits paths, so every branch must reach ${joinKind === 'if-join' ? 'End if' : 'Fan in'} before continuing.`;
    }
  }

  return null;
}

function executionNodeLabel(node: WorkbenchNode): string {
  if (node.data.kind === 'start') {
    return 'Start';
  }

  if (node.data.kind === 'command') {
    return node.data.command.id;
  }

  if (node.data.kind === 'route') {
    return node.data.label;
  }

  return node.id;
}

function canReachEnd(sourceId: string, edges: WorkbenchEdge[], visited = new Set<string>()): boolean {
  if (sourceId === 'end') {
    return true;
  }

  if (visited.has(sourceId)) {
    return false;
  }

  visited.add(sourceId);
  return edges
    .filter((edge) => edge.source === sourceId && edge.sourceHandle === 'exec-out')
    .some((edge) => Boolean(edge.target) && canReachEnd(edge.target as string, edges, visited));
}

function canReachRouteJoin(
  sourceId: string,
  joinKind: RouteNodeData['routeKind'],
  nodes: WorkbenchNode[],
  edges: WorkbenchEdge[],
  visited = new Set<string>(),
): boolean {
  const node = nodes.find((candidate) => candidate.id === sourceId);
  if (node?.data.kind === 'route' && node.data.routeKind === joinKind) {
    return true;
  }

  if (sourceId === 'end' || visited.has(sourceId)) {
    return false;
  }

  visited.add(sourceId);
  return edges
    .filter((edge) => edge.source === sourceId && edge.sourceHandle === 'exec-out')
    .some((edge) => Boolean(edge.target) && canReachRouteJoin(edge.target as string, joinKind, nodes, edges, visited));
}

function enqueueExecutionEdges(
  sourceId: string,
  edges: WorkbenchEdge[],
  variables: Map<string, string>,
  activeEdges: Set<string>,
  queuedEdges: Set<string>,
): WorkbenchEdge[] {
  const outgoing = eligibleExecutionEdges(sourceId, edges, variables).filter((edge) => !queuedEdges.has(edge.id));
  for (const edge of outgoing) {
    activeEdges.add(edge.id);
    queuedEdges.add(edge.id);
  }

  return outgoing;
}

function eligibleExecutionEdges(
  sourceId: string,
  edges: WorkbenchEdge[],
  variables: Map<string, string>,
): WorkbenchEdge[] {
  return edges.filter((edge) => {
    if (edge.source !== sourceId || edge.sourceHandle !== 'exec-out') {
      return false;
    }

    if (edge.data?.mode === 'if' || edge.data?.mode === 'while') {
      return evaluateExpression(edge.data.expression ?? '', variables);
    }

    return true;
  });
}

function markEdge(edges: WorkbenchEdge[], edgeId: string, status: NonNullable<WorkbenchEdge['data']>['status']): WorkbenchEdge[] {
  return edges.map((edge) =>
    edge.id === edgeId
      ? applyEdgePresentation({ ...edge, data: { ...edge.data, status } })
      : edge,
  );
}

function selectExecutionEdge(
  currentNodeId: string,
  nodes: WorkbenchNode[],
  edges: WorkbenchEdge[],
  variables: Map<string, string>,
): WorkbenchEdge | undefined {
  const outgoing = edges.filter((edge) => edge.source === currentNodeId && edge.sourceHandle === 'exec-out');
  const conditional = outgoing.filter((edge) => edge.data?.mode === 'if' || edge.data?.mode === 'while');
  const always = outgoing.find((edge) => !edge.data?.mode || edge.data.mode === 'always');

  for (const edge of conditional) {
    if (evaluateExpression(edge.data?.expression ?? '', variables)) {
      return edge;
    }
  }

  return always;
}

function nextExecutionCurrent(
  edge: WorkbenchEdge,
  commandNodeId: string,
  nodes: WorkbenchNode[],
  variables: Map<string, string>,
): string {
  if (edge.data?.mode !== 'while') {
    return commandNodeId;
  }

  const maxIterations = edge.data.maxIterations ?? 10;
  const countKey = `__loop:${edge.id}`;
  const currentCount = Number(variables.get(countKey) ?? '0') + 1;
  variables.set(countKey, String(currentCount));

  if (currentCount < maxIterations && evaluateExpression(edge.data.expression ?? '', variables)) {
    return edge.source;
  }

  variables.set(countKey, '0');
  return commandNodeId;
}

function syncVariables(nodes: WorkbenchNode[], variables: Map<string, string>) {
  for (const node of nodes) {
    if (node.data.kind === 'variable') {
      variables.set(node.data.name, node.data.value);
    }
  }
}

function evaluateExpression(expression: string, variables: Map<string, string>): boolean {
  const text = expression.trim();
  if (!text) {
    return true;
  }

  if (text.startsWith('!')) {
    return !truthy(variables.get(text.slice(1).trim()) ?? '');
  }

  const match = text.match(/^([A-Za-z_][\w.-]*)\s*(==|!=|>=|<=|>|<)\s*(.+)$/);
  if (!match) {
    return truthy(variables.get(text) ?? text);
  }

  const [, name, op, rawExpected] = match;
  const actualRaw = variables.get(name) ?? '';
  const expectedRaw = rawExpected.replace(/^["']|["']$/g, '');
  const actualNumber = Number(actualRaw);
  const expectedNumber = Number(expectedRaw);
  const numeric = Number.isFinite(actualNumber) && Number.isFinite(expectedNumber);

  const actual = numeric ? actualNumber : actualRaw.toLowerCase();
  const expected = numeric ? expectedNumber : expectedRaw.toLowerCase();

  switch (op) {
    case '==':
      return actual === expected;
    case '!=':
      return actual !== expected;
    case '>':
      return actual > expected;
    case '<':
      return actual < expected;
    case '>=':
      return actual >= expected;
    case '<=':
      return actual <= expected;
    default:
      return false;
  }
}

function truthy(value: string): boolean {
  return value !== '' && value !== '0' && value.toLowerCase() !== 'false' && value.toLowerCase() !== 'null';
}

function edgeClassName(edge: WorkbenchEdge): string {
  if (handleKind(edge.sourceHandle) === 'data-out') {
    return 'edge-data';
  }

  const locked = edge.data?.locked ? 'edge-locked' : '';
  if (edge.data?.mode === 'if') {
    return `edge-exec edge-if ${locked} ${edgeStatusClass(edge)}`;
  }

  if (edge.data?.mode === 'while') {
    return `edge-exec edge-while ${locked} ${edgeStatusClass(edge)}`;
  }

  return `edge-exec ${locked} ${edgeStatusClass(edge)}`;
}

function edgeStatusClass(edge: WorkbenchEdge): string {
  return edge.data?.status ? `edge-${edge.data.status}` : '';
}

function applyEdgePresentation(edge: WorkbenchEdge): WorkbenchEdge {
  const presented: WorkbenchEdge = {
    ...edge,
    className: edgeClassName(edge),
    interactionWidth: edge.data?.locked ? 28 : edge.interactionWidth,
  };

  if (handleKind(edge.sourceHandle) === 'exec-out') {
    const label = executionEdgeLabel(edge);
    return {
      ...presented,
      label,
      labelStyle: {
        fill: edge.data?.mode === 'always' ? '#8f9caf' : '#f4f7fb',
        fontFamily: 'DM Mono, monospace',
        fontSize: 10,
        fontWeight: 600,
        letterSpacing: 0,
      },
      labelBgStyle: {
        fill: edgeLabelBackground(edge),
        fillOpacity: 0.92,
      },
      labelBgPadding: [5, 3],
      labelBgBorderRadius: 3,
    };
  }

  if (handleKind(edge.sourceHandle) === 'data-out') {
    return {
      ...presented,
      label: handleName(edge.sourceHandle),
      labelStyle: {
        fill: '#86d8f8',
        fontFamily: 'DM Mono, monospace',
        fontSize: 9,
        fontWeight: 500,
        letterSpacing: 0,
      },
      labelBgStyle: {
        fill: '#101923',
        fillOpacity: 0.85,
      },
      labelBgPadding: [4, 2],
      labelBgBorderRadius: 3,
    };
  }

  return presented;
}

function executionEdgeLabel(edge: WorkbenchEdge): string {
  if (edge.data?.label) {
    return edge.data.label;
  }

  const mode = edge.data?.mode ?? 'always';
  if (mode === 'always') {
    return 'NEXT';
  }

  const expression = edge.data?.expression?.trim() || 'true';
  if (mode === 'while') {
    return `WHILE ${compactExpressionLabel(expression)}`;
  }

  const branch = edge.data?.branch ?? branchFromExpression(expression);
  const subject = booleanSubject(expression);
  if (branch) {
    return `IF ${subject} = ${branch}`;
  }

  return `IF ${compactExpressionLabel(expression)}`;
}

function edgeLabelBackground(edge: WorkbenchEdge): string {
  if (edge.data?.mode === 'if') {
    return edge.data.branch === 'false' || branchFromExpression(edge.data.expression ?? '') === 'false' ? '#4a2f12' : '#2d3719';
  }

  if (edge.data?.mode === 'while') {
    return '#341b2d';
  }

  return '#121923';
}

function normalizeLogicPatch(
  edge: WorkbenchEdge,
  patch: { mode?: LogicEdgeMode; expression?: string; branch?: 'true' | 'false'; maxIterations?: number },
  nodes: WorkbenchNode[],
): WorkbenchEdge['data'] {
  const current = edge.data ?? {};
  const mode = patch.mode ?? current.mode ?? 'always';
  let expression = patch.expression ?? current.expression ?? defaultBooleanExpression(nodes);
  let branch: 'true' | 'false' | undefined = patch.branch ?? current.branch ?? branchFromExpression(expression) ?? 'true';

  if (mode === 'always') {
    expression = '';
    branch = undefined;
  } else if (mode === 'if') {
    expression = normalizeBooleanExpression(expression || defaultBooleanExpression(nodes), branch);
  } else if (mode === 'while') {
    branch = undefined;
    expression = expression || defaultBooleanExpression(nodes);
  }

  return {
    ...current,
    ...patch,
    mode,
    expression,
    branch,
    maxIterations: patch.maxIterations ?? current.maxIterations ?? 10,
  };
}

function defaultBooleanExpression(nodes: WorkbenchNode[]): string {
  const booleanVariable = nodes.find((node) => node.data.kind === 'variable' && node.data.valueType === 'Boolean');
  return booleanVariable?.data.kind === 'variable' ? booleanVariable.data.name : 'status';
}

function normalizeBooleanExpression(expression: string, branch: 'true' | 'false'): string {
  const subject = booleanSubject(expression);
  return branch === 'false' ? `${subject} == false` : subject;
}

function branchFromExpression(expression: string): 'true' | 'false' | undefined {
  const trimmed = expression.trim().toLowerCase();
  if (!trimmed) {
    return undefined;
  }

  if (trimmed.startsWith('!')) {
    return 'false';
  }

  const comparison = trimmed.match(/^.+?\s*(==|!=)\s*(true|false)$/);
  if (!comparison) {
    return 'true';
  }

  const [, operator, value] = comparison;
  if (operator === '==') {
    return value as 'true' | 'false';
  }

  return value === 'true' ? 'false' : 'true';
}

function booleanSubject(expression: string): string {
  const trimmed = expression.trim();
  if (!trimmed) {
    return 'status';
  }

  if (trimmed.startsWith('!')) {
    return trimmed.slice(1).trim() || 'status';
  }

  const comparison = trimmed.match(/^(.+?)\s*(==|!=)\s*(true|false)$/i);
  return comparison ? comparison[1].trim() : trimmed;
}

function compactExpressionLabel(expression: string): string {
  return expression.trim().replace(/\s+/g, ' ') || 'true';
}

function replaceVariableInExpression(expression: string, oldName: string, newName: string): string {
  if (!isIdentifier(oldName) || !isIdentifier(newName)) {
    return expression;
  }

  return expression.replace(new RegExp(`\\b${escapeRegExp(oldName)}\\b`, 'g'), newName);
}

function isIdentifier(value: string): boolean {
  return /^[A-Za-z_]\w*$/.test(value);
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function lastExecutionSource(nodes: WorkbenchNode[], edges: WorkbenchEdge[]): string {
  const order = executionOrder(nodes, edges);
  return order.at(-1) ?? 'start';
}

function markCommand(nodes: WorkbenchNode[], nodeId: string, patch: Partial<CommandNodeData>): WorkbenchNode[] {
  return nodes.map((node) =>
    node.id === nodeId && node.data.kind === 'command'
      ? { ...node, data: { ...node.data, ...patch } }
      : node,
  );
}

function markRoute(nodes: WorkbenchNode[], nodeId: string, patch: Partial<RouteNodeData>): WorkbenchNode[] {
  return nodes.map((node) =>
    node.id === nodeId && node.data.kind === 'route'
      ? { ...node, data: { ...node.data, ...patch } }
      : node,
  );
}

function isCommandNode(node: WorkbenchNode): node is WorkbenchNode & { data: CommandNodeData } {
  return node.data.kind === 'command';
}

function buildValues(parameters: ParameterDocument[], rawValues: Record<string, string>): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const parameter of parameters) {
    const parsed = parseInput(parameter, rawValues[parameter.name] ?? '');
    if (parsed !== undefined) {
      values[parameter.name] = parsed;
    }
  }
  return values;
}

function dataInHandle(parameter: ParameterDocument): string {
  return `data-in:${parameter.name}:${parameterType(parameter)}`;
}

function dataOutHandle(parameter: ParameterDocument): string {
  return `data-out:${parameter.name}:${parameterType(parameter)}`;
}

function variableInHandle(type: string): string {
  return `data-in:value:${type}`;
}

function variableOutHandle(type: string): string {
  return `data-out:value:${type}`;
}

function handleKind(handle: string | null | undefined): string {
  return handle?.split(':')[0] ?? '';
}

function handleName(handle: string | null | undefined): string {
  return handle?.split(':')[1] ?? '';
}

function handleType(handle: string | null | undefined): string {
  return handle?.split(':').slice(2).join(':') ?? '';
}

function parameterType(parameter: ParameterDocument): string {
  const kind = parameter.type === 'Double' ? 'Float' : parameter.type;
  return `${kind}${parameter.array ? '[]' : ''}`;
}

function display(value: string, fallback: string): string {
  return value && value !== 'TODO' ? value : fallback;
}

function normalizeDefault(value: string | null): string {
  return !value || value.toLowerCase() === 'null' ? '' : value;
}

function label(parameter: ParameterDocument): string {
  return `${display(parameter.displayName, parameter.name)}${parameter.required ? '' : ' (optional)'}`;
}

function placeholder(parameter: ParameterDocument): string {
  return parameterType(parameter);
}

function inputSummary(command: CommandDocument): string {
  return command.inputs.length === 0
    ? 'no inputs'
    : command.inputs.map((input) => `${input.name}:${parameterType(input)}`).join(', ');
}

function delay(milliseconds: number) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}

function formatWorkflowScript(script: string): string {
  const compact = script
    .replace(/\{\s*/g, '{\n')
    .replace(/\s*\}/g, '\n}')
    .replace(/\)\s*\.Else/g, ')\n.Else')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);

  let indent = 0;
  const lines: string[] = [];
  for (const line of compact) {
    if (line.startsWith('}') || line.startsWith('.Else')) {
      indent = Math.max(0, indent - 1);
    }

    lines.push(`${'    '.repeat(indent)}${line}`);

    if (line.endsWith('{') || line.includes('=> {')) {
      indent += 1;
    }
  }

  return `${lines.join('\n')}\n`;
}

function highlightScriptLine(line: string) {
  const tokenRegex = /(TinyVariable|TinyBench|If|Else|new|out|Outputs|true|false|null)|([A-Z]\w*)\.(\w+)|("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')|(==|!=|>=|<=|=>|[=(){}.,<>])|(\b\d+(?:\.\d+)?\b)/g;
  const parts: ReactNode[] = [];
  let cursor = 0;
  let match: RegExpExecArray | null;

  while ((match = tokenRegex.exec(line)) !== null) {
    if (match.index > cursor) {
      parts.push(<span key={`${cursor}-text`}>{line.slice(cursor, match.index)}</span>);
    }

    const [token, keyword, device, command, quoted, operator, number] = match;
    if (keyword) {
      parts.push(<span className="tok-keyword" key={`${match.index}-keyword`}>{token}</span>);
    } else if (device && command) {
      parts.push(<span className="tok-device" key={`${match.index}-device`}>{device}</span>);
      parts.push(<span className="tok-operator" key={`${match.index}-dot`}>.</span>);
      parts.push(<span className="tok-command" key={`${match.index}-command`}>{command}</span>);
    } else if (quoted) {
      parts.push(<span className="tok-string" key={`${match.index}-string`}>{token}</span>);
    } else if (operator) {
      parts.push(<span className="tok-operator" key={`${match.index}-operator`}>{token}</span>);
    } else if (number) {
      parts.push(<span className="tok-number" key={`${match.index}-number`}>{token}</span>);
    }

    cursor = match.index + token.length;
  }

  if (cursor < line.length) {
    parts.push(<span key={`${cursor}-tail`}>{line.slice(cursor)}</span>);
  }

  return parts.length > 0 ? parts : ' ';
}

function getCompletionState(
  editor: HTMLTextAreaElement,
  script: string,
  devices: AddedDevice[],
  definitionCache: Record<string, AddedDevice>,
): CompletionState | null {
  const cursor = editor.selectionStart;
  const beforeCursor = script.slice(0, cursor);

  const commandCallMatch = beforeCursor.match(/(?:^|[^\w.])([A-Za-z]\w*)\.([A-Za-z]\w*)\(([^()\n]*)$/);
  if (commandCallMatch) {
    const [, alias, commandId, args] = commandCallMatch;
    const device = findCompletionDevice(alias, devices, definitionCache);
    const command = device?.definition.commands.find((candidate) => candidate.id.toLowerCase() === commandId.toLowerCase());
    const argumentMatch = args.match(/(?:^|,)\s*(out\s+)?(\w*)$/i);
    if (!command || command.outputs.length === 0 || !argumentMatch) {
      return null;
    }

    const [, outPrefix = '', partial] = argumentMatch;
    const usedOutputs = new Set(
      Array.from(args.matchAll(/\bout\s+(\w+)\s*=/gi)).map((match) => match[1].toLowerCase()),
    );
    const normalizedPartial = partial.toLowerCase();
    const needsOutPrefix = outPrefix.length === 0;

    return createCompletionState(
      editor,
      beforeCursor,
      cursor,
      partial,
      command.outputs
        .filter((output) => !usedOutputs.has(output.name.toLowerCase()))
        .filter((output) => output.name.toLowerCase().startsWith(normalizedPartial))
        .map((output) => ({
          label: `out ${output.name}`,
          detail: parameterType(output),
          insertText: `${needsOutPrefix ? 'out ' : ''}${output.name} = `,
        })),
    );
  }

  const outputMatch = beforeCursor.match(/(?:^|[^\w.])([A-Za-z]\w*)\.([A-Za-z]\w*)\.Outputs\.(\w*)$/);
  if (outputMatch) {
    const [, alias, commandId, partial] = outputMatch;
    const device = findCompletionDevice(alias, devices, definitionCache);
    const command = device?.definition.commands.find((candidate) => candidate.id.toLowerCase() === commandId.toLowerCase());
    if (!command) {
      return null;
    }

    return createCompletionState(
      editor,
      beforeCursor,
      cursor,
      partial,
      command.outputs
        .filter((output) => output.name.toLowerCase().startsWith(partial.toLowerCase()))
        .map((output) => ({
          label: output.name,
          detail: parameterType(output),
          insertText: output.name,
        })),
    );
  }

  const commandMemberMatch = beforeCursor.match(/(?:^|[^\w.])([A-Za-z]\w*)\.([A-Za-z]\w*)\.(\w*)$/);
  if (commandMemberMatch) {
    const [, alias, commandId, partial] = commandMemberMatch;
    const device = findCompletionDevice(alias, devices, definitionCache);
    const command = device?.definition.commands.find((candidate) => candidate.id.toLowerCase() === commandId.toLowerCase());
    if (!command || command.outputs.length === 0 || !'outputs'.startsWith(partial.toLowerCase())) {
      return null;
    }

    return createCompletionState(editor, beforeCursor, cursor, partial, [
      {
        label: 'Outputs',
        detail: `${command.outputs.length} outputs`,
        insertText: 'Outputs.',
      },
    ]);
  }

  const commandMatch = beforeCursor.match(/(?:^|[^\w.])([A-Za-z]\w*)\.(\w*)$/);
  if (!commandMatch) {
    return null;
  }

  const [, alias, partial] = commandMatch;
  const device = findCompletionDevice(alias, devices, definitionCache);
  if (!device) {
    return null;
  }

  const normalizedPartial = partial.toLowerCase();
  const items = device.definition.commands
    .filter((command) => command.id.toLowerCase().startsWith(normalizedPartial))
    .map((command) => ({
      label: command.id,
      detail: commandCompletionDetail(command),
      insertText: `${command.id}()`,
    }));

  return createCompletionState(editor, beforeCursor, cursor, partial, items);
}

function commandCompletionDetail(command: CommandDocument): string {
  const inputs = command.inputs.map((input) => `${input.name}:${parameterType(input)}`);
  const outputs = command.outputs.map((output) => `out ${output.name}:${parameterType(output)}`);
  const parts = [...inputs, ...outputs];
  return parts.length === 0 ? 'no inputs' : parts.join(', ');
}

function createCompletionState(
  editor: HTMLTextAreaElement,
  beforeCursor: string,
  cursor: number,
  partial: string,
  items: CompletionItem[],
): CompletionState | null {
  if (items.length === 0) {
    return null;
  }

  const beforeLine = beforeCursor.slice(beforeCursor.lastIndexOf('\n') + 1);
  const charWidth = 7.25;
  const rowHeight = 18;
  const line = beforeCursor.split('\n').length;
  return {
    items,
    selectedIndex: 0,
    replaceFrom: cursor - partial.length,
    replaceTo: cursor,
    top: line * rowHeight + 8 - editor.scrollTop,
    left: Math.min(280, 10 + beforeLine.length * charWidth - editor.scrollLeft),
  };
}

function findCompletionDevice(
  alias: string,
  devices: AddedDevice[],
  definitionCache: Record<string, AddedDevice>,
): AddedDevice | undefined {
  const loaded = devices.find((device) => deviceMatchesCompletionAlias(device, alias));
  if (loaded) {
    return loaded;
  }

  return Object.values(definitionCache).find((device) => deviceMatchesCompletionAlias(device, alias));
}

async function buildGraphFromScript(
  script: string,
  currentDevices: AddedDevice[],
  catalog: CatalogDevice[],
  startingSequence: number,
): Promise<{ devices: AddedDevice[]; nodes: WorkbenchNode[]; edges: WorkbenchEdge[]; nextSequence: number }> {
  let devices = [...currentDevices];
  let sequence = startingSequence;
  const nodes: WorkbenchNode[] = [startNode, endNode];
  const edges: WorkbenchEdge[] = [];
  const commandNodes = new Map<string, WorkbenchNode & { data: CommandNodeData }>();
  const variableNodes = new Map<string, WorkbenchNode & { data: VariableNodeData }>();

  const commandCalls = parseCommandCalls(script).filter((call) => call.alias.toLowerCase() !== 'tinybench');
  if (commandCalls.length === 0) {
    throw new Error('no device command calls found');
  }

  const ifBlock = parseIfBlock(script);
  const branchKeys = new Set<string>();
  if (ifBlock) {
    branchKeys.add(`${ifBlock.thenCall.alias}.${ifBlock.thenCall.command}`);
    if (ifBlock.elseCall) {
      branchKeys.add(`${ifBlock.elseCall.alias}.${ifBlock.elseCall.command}`);
    }
  }

  const preCommands = commandCalls.filter((call) => !branchKeys.has(`${call.alias}.${call.command}`));
  let previousExecutionSource = 'start';

  for (const call of preCommands) {
    const result = await ensureDeviceForAlias(call.alias, devices, catalog);
    devices = result.devices;
    const node = createCommandNode(++sequence, result.device, call.command, { x: 320, y: 150 + commandNodes.size * 170 });
    if (!node) {
      throw new Error(`unknown command ${call.alias}.${call.command}`);
    }

    nodes.push(node);
    commandNodes.set(`${call.alias}.${call.command}`, node);
    edges.push(createExecutionEdge(previousExecutionSource, node.id));
    previousExecutionSource = node.id;
  }

  for (const variable of parseVariables(script)) {
    const sourceKey = `${variable.alias}.${variable.command}`;
    const sourceNode = commandNodes.get(sourceKey);
    const sourceOutput = sourceNode?.data.command.outputs.find((output) => output.name === variable.output);
    if (!sourceNode) {
      throw new Error(`variable ${variable.name} references command ${sourceKey}, but that command is not run before it`);
    }
    if (!sourceOutput) {
      throw new Error(`variable ${variable.name} references missing output ${sourceKey}.Outputs.${variable.output}`);
    }
    const valueType = sourceOutput ? parameterType(sourceOutput) : 'String';
    const variableNode = createVariableNode(++sequence, variable.name, valueType, { x: 680, y: 150 + variableNodes.size * 125 });
    nodes.push(variableNode);
    variableNodes.set(variable.name, variableNode);

    if (sourceNode && sourceOutput) {
      edges.push(createDataEdge(sourceNode.id, dataOutHandle(sourceOutput), variableNode.id, variableInHandle(valueType)));
    }
  }

  for (const call of preCommands) {
    const sourceNode = commandNodes.get(`${call.alias}.${call.command}`);
    if (!sourceNode) {
      continue;
    }

    for (const assignment of parseOutAssignments(call.args)) {
      const variableNode = variableNodes.get(assignment.variableName);
      const output = sourceNode.data.command.outputs.find((candidate) => candidate.name === assignment.outputName);
      if (variableNode && output) {
        edges.push(createDataEdge(sourceNode.id, dataOutHandle(output), variableNode.id, variableInHandle(parameterType(output))));
      }
    }
  }

  if (ifBlock) {
    const ifY = 170 + preCommands.length * 170;
    const ifSplitNode = createRouteNode(++sequence, 'if-split', '', { x: 385, y: ifY - 70 }, ifBlock.expression);
    const ifJoinNode = createRouteNode(++sequence, 'if-join', '', { x: 385, y: ifY + 145 }, ifBlock.expression);
    nodes.push(ifSplitNode, ifJoinNode);
    edges.push(createExecutionEdge(previousExecutionSource, ifSplitNode.id, {
      mode: 'always',
      expression: ifBlock.expression,
      label: `IF ${compactExpressionLabel(ifBlock.expression)}`,
      locked: true,
    }));

    const thenDevice = await ensureDeviceForAlias(ifBlock.thenCall.alias, devices, catalog);
    devices = thenDevice.devices;
    const thenNode = createCommandNode(++sequence, thenDevice.device, ifBlock.thenCall.command, { x: 220, y: ifY });
    if (!thenNode) {
      throw new Error(`unknown command ${ifBlock.thenCall.alias}.${ifBlock.thenCall.command}`);
    }
    nodes.push(thenNode);
    edges.push(createExecutionEdge(ifSplitNode.id, thenNode.id, {
      mode: 'if',
      expression: ifBlock.expression,
      branch: branchFromExpression(ifBlock.expression) ?? 'true',
      label: 'TRUE',
      locked: true,
    }));
    edges.push(createExecutionEdge(thenNode.id, ifJoinNode.id, { mode: 'always', expression: '', label: 'JOIN', locked: true }));

    if (ifBlock.elseCall) {
      const elseDevice = await ensureDeviceForAlias(ifBlock.elseCall.alias, devices, catalog);
      devices = elseDevice.devices;
      const elseNode = createCommandNode(++sequence, elseDevice.device, ifBlock.elseCall.command, { x: 540, y: ifY });
      if (!elseNode) {
        throw new Error(`unknown command ${ifBlock.elseCall.alias}.${ifBlock.elseCall.command}`);
      }
      nodes.push(elseNode);
      const elseExpression = negateExpression(ifBlock.expression);
      edges.push(createExecutionEdge(ifSplitNode.id, elseNode.id, {
        mode: 'if',
        expression: elseExpression,
        branch: branchFromExpression(elseExpression) ?? 'false',
        label: 'FALSE',
        locked: true,
      }));
      edges.push(createExecutionEdge(elseNode.id, ifJoinNode.id, { mode: 'always', expression: '', label: 'JOIN', locked: true }));
    }

    edges.push(createExecutionEdge(ifJoinNode.id, 'end', { mode: 'always', expression: '', label: 'NEXT', locked: true }));
  } else if (previousExecutionSource !== 'start') {
    edges.push(createExecutionEdge(previousExecutionSource, 'end'));
  }

  if (nodes.length === 2) {
    throw new Error('script did not produce any workflow blocks');
  }

  return { devices, nodes, edges: dedupeEdges(edges), nextSequence: sequence };
}

function dedupeEdges(edges: WorkbenchEdge[]): WorkbenchEdge[] {
  return Array.from(new Map(edges.map((edge) => [edge.id, edge])).values());
}

function removeNodeFromEdges(nodeId: string, edges: WorkbenchEdge[]): WorkbenchEdge[] {
  const incoming = edges.filter((edge) => edge.target === nodeId);
  const outgoing = edges.filter((edge) => edge.source === nodeId);
  const remaining = edges.filter((edge) => edge.source !== nodeId && edge.target !== nodeId);

  const lockedIncoming = incoming.find((edge) => edge.sourceHandle === 'exec-out' && edge.data?.locked);
  const lockedOutgoing = outgoing.find((edge) => edge.targetHandle === 'exec-in' && edge.data?.locked);
  if (!lockedIncoming || !lockedOutgoing?.target) {
    return remaining;
  }

  return dedupeEdges([
    ...remaining,
    createExecutionEdge(lockedIncoming.source, lockedOutgoing.target, {
      mode: lockedIncoming.data?.mode ?? 'always',
      expression: lockedIncoming.data?.expression ?? '',
      branch: lockedIncoming.data?.branch,
      label: lockedIncoming.data?.label,
      locked: true,
    }),
  ]);
}

function parseVariables(script: string): Array<{ name: string; alias: string; command: string; output: string }> {
  const variables: Array<{ name: string; alias: string; command: string; output: string }> = [];
  const regex = /TinyVariable\s+(\w+)\s*=\s*new\s+TinyVariable\s*\(\s*(\w+)\.(\w+)\.Outputs\.(\w+)\s*\)/g;
  for (const match of script.matchAll(regex)) {
    variables.push({ name: match[1], alias: match[2], command: match[3], output: match[4] });
  }
  return variables;
}

function parseCommandCalls(script: string): Array<{ alias: string; command: string; args: string }> {
  const calls: Array<{ alias: string; command: string; args: string }> = [];
  const regex = /(\w+)\.(\w+)\s*\(([^)]*)\)/g;
  for (const match of script.matchAll(regex)) {
    calls.push({ alias: match[1], command: match[2], args: match[3] });
  }
  return calls;
}

function parseOutAssignments(args: string): Array<{ outputName: string; variableName: string }> {
  const assignments: Array<{ outputName: string; variableName: string }> = [];
  const regex = /out\s+(\w+)\s*=\s*(\w+)/g;
  for (const match of args.matchAll(regex)) {
    assignments.push({ outputName: match[1], variableName: match[2] });
  }
  return assignments;
}

function parseIfBlock(script: string):
  | { expression: string; thenCall: { alias: string; command: string }; elseCall?: { alias: string; command: string } }
  | undefined {
  const ifMatch = script.match(/TinyBench\.If\s*\(\s*\(?\s*(.*?)\s*\)?\s*=>\s*\{\s*(\w+)\.(\w+)\s*\([^)]*\)\s*\}/s);
  if (!ifMatch) {
    return undefined;
  }

  const elseMatch = script.match(/\.Else\s*\(\s*(\w+)\.(\w+)\s*\([^)]*\)\s*\)/s);
  return {
    expression: ifMatch[1].trim(),
    thenCall: { alias: ifMatch[2], command: ifMatch[3] },
    elseCall: elseMatch ? { alias: elseMatch[1], command: elseMatch[2] } : undefined,
  };
}

async function ensureDeviceForAlias(
  alias: string,
  devices: AddedDevice[],
  catalog: CatalogDevice[],
): Promise<{ device: AddedDevice; devices: AddedDevice[] }> {
  const existing = devices.find((device) => deviceMatchesAlias(device, alias));
  if (existing) {
    return { device: existing, devices };
  }

  const catalogDevice = catalog.find((device) => deviceMatchesAlias(device, alias));
  if (!catalogDevice) {
    throw new Error(`unknown device alias ${alias}`);
  }
  const definition = await getDefinition(catalogDevice.hostUrl);
  const added: AddedDevice = {
    ...catalogDevice,
    name: display(definition.driver.displayName, catalogDevice.name),
    definition,
    connected: false,
    connectionInputs: Object.fromEntries(
      definition.connection.parameters.map((parameter) => [parameter.name, normalizeDefault(parameter.default)]),
    ),
  };
  return { device: added, devices: [...devices, added] };
}

function createCommandNode(
  sequence: number,
  device: AddedDevice,
  commandId: string,
  position: { x: number; y: number },
): (WorkbenchNode & { data: CommandNodeData }) | undefined {
  const command = device.definition.commands.find((candidate) => candidate.id.toLowerCase() === commandId.toLowerCase());
  if (!command) {
    return undefined;
  }

  return {
    id: `command:${sequence}`,
    type: 'command',
    position,
    data: {
      kind: 'command',
      deviceId: device.id,
      deviceName: device.name,
      hostUrl: device.hostUrl,
      command,
      inputs: Object.fromEntries(command.inputs.map((input) => [input.name, normalizeDefault(input.default)])),
      status: 'ready',
      outputs: {},
    },
  };
}

function createVariableNode(
  sequence: number,
  name: string,
  valueType: string,
  position: { x: number; y: number },
): WorkbenchNode & { data: VariableNodeData } {
  return {
    id: `variable:${sequence}`,
    type: 'variable',
    position,
    data: { kind: 'variable', name, valueType, value: '' },
  };
}

function createRouteNode(
  sequence: number,
  routeKind: RouteNodeData['routeKind'],
  label: string,
  position: { x: number; y: number },
  expression?: string,
): WorkbenchNode & { data: RouteNodeData } {
  return {
    id: `route:${sequence}`,
    type: 'route',
    position,
    data: { kind: 'route', routeKind, label, expression, status: 'ready' },
  };
}

function insertCommandIntoExecutionPath(
  edges: WorkbenchEdge[],
  nodes: WorkbenchNode[],
  commandNodeId: string,
  position: { x: number; y: number },
): WorkbenchEdge[] | null {
  const nearest = nearestExecutionEdge(edges, nodes, position);
  if (!nearest || !nearest.target) {
    return null;
  }

  const before = createExecutionEdge(nearest.source, commandNodeId, {
    mode: nearest.data?.mode ?? 'always',
    expression: nearest.data?.expression ?? '',
    branch: nearest.data?.branch,
    label: nearest.data?.label,
    locked: nearest.data?.locked,
  });
  const after = createExecutionEdge(commandNodeId, nearest.target, {
    mode: 'always',
    expression: '',
    label: nearest.data?.locked ? 'JOIN' : 'NEXT',
    locked: nearest.data?.locked,
  });

  return [...edges.filter((edge) => edge.id !== nearest.id), before, after];
}

function insertCommandAtEdge(edges: WorkbenchEdge[], edgeId: string, commandNodeId: string): WorkbenchEdge[] | null {
  const edge = edges.find((candidate) => candidate.id === edgeId);
  if (!edge?.target) {
    return null;
  }

  const before = createExecutionEdge(edge.source, commandNodeId, {
    mode: edge.data?.mode ?? 'always',
    expression: edge.data?.expression ?? '',
    branch: edge.data?.branch,
    label: edge.data?.label,
    locked: edge.data?.locked,
  });
  const after = createExecutionEdge(commandNodeId, edge.target, {
    mode: 'always',
    expression: '',
    label: edge.data?.locked ? 'JOIN' : 'NEXT',
    locked: edge.data?.locked,
  });

  return dedupeEdges([...edges.filter((candidate) => candidate.id !== edge.id), before, after]);
}

function nearestExecutionEdge(
  edges: WorkbenchEdge[],
  nodes: WorkbenchNode[],
  position: { x: number; y: number },
): WorkbenchEdge | null {
  const candidates = edges
    .filter((edge) => edge.sourceHandle === 'exec-out' && edge.targetHandle === 'exec-in')
    .map((edge) => {
      const source = nodes.find((node) => node.id === edge.source);
      const target = nodes.find((node) => node.id === edge.target);
      if (!source || !target) {
        return null;
      }

      const midpoint = {
        x: (source.position.x + target.position.x) / 2,
        y: (source.position.y + target.position.y) / 2,
      };
      return { edge, distance: Math.hypot(position.x - midpoint.x, position.y - midpoint.y) };
    })
    .filter((candidate): candidate is { edge: WorkbenchEdge; distance: number } => candidate !== null)
    .sort((a, b) => a.distance - b.distance);

  const nearest = candidates[0];
  return nearest && nearest.distance < 140 ? nearest.edge : null;
}

function createExecutionEdge(
  source: string,
  target: string,
  logic?: { mode: LogicEdgeMode; expression: string; branch?: 'true' | 'false'; label?: string; locked?: boolean },
): WorkbenchEdge {
  const edge: WorkbenchEdge = {
    id: `${source}:exec-out->${target}:exec-in:${logic?.mode ?? 'always'}:${logic?.expression ?? ''}`,
    source,
    sourceHandle: 'exec-out',
    target,
    targetHandle: 'exec-in',
    type: 'smoothstep',
    animated: true,
    data: {
      mode: logic?.mode ?? 'always',
      expression: logic?.expression ?? '',
      branch: logic?.branch,
      label: logic?.label,
      locked: logic?.locked,
      maxIterations: 10,
    },
    markerEnd: { type: MarkerType.ArrowClosed },
  };
  return applyEdgePresentation(edge);
}

function createDataEdge(source: string, sourceHandle: string, target: string, targetHandle: string): WorkbenchEdge {
  return applyEdgePresentation({
    id: `${source}:${sourceHandle}->${target}:${targetHandle}`,
    source,
    sourceHandle,
    target,
    targetHandle,
    className: 'edge-data',
    markerEnd: { type: MarkerType.ArrowClosed },
  });
}

function deviceMatchesAlias(device: { id: string; name: string }, alias: string): boolean {
  const normalized = alias.toLowerCase().replace(/[^a-z0-9]/g, '');
  const id = device.id.toLowerCase().replace(/[^a-z0-9]/g, '');
  const name = device.name.toLowerCase().replace(/[^a-z0-9]/g, '');
  return id === normalized || name.includes(normalized) || normalized.includes(id);
}

function deviceMatchesCompletionAlias(device: AddedDevice, alias: string): boolean {
  const normalized = normalizeAlias(alias);
  const driverClassName = device.definition.driver.class.split('.').at(-1) ?? device.definition.driver.class;
  const driverClassAlias = driverClassName.replace(/Driver$/i, '');
  const firstNameToken = device.name.split(/\s+/)[0] ?? '';
  const candidates = [
    device.id,
    device.name,
    firstNameToken,
    device.definition.driver.id,
    driverClassName,
    driverClassAlias,
  ];

  return candidates.map(normalizeAlias).some((candidate) => candidate === normalized);
}

function normalizeAlias(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, '');
}

function negateExpression(expression: string): string {
  const match = expression.match(/^(.+?)\s*(==|!=|>=|<=|>|<)\s*(.+)$/);
  if (!match) {
    return `!${expression}`;
  }

  const inverse: Record<string, string> = {
    '==': '!=',
    '!=': '==',
    '>': '<=',
    '<': '>=',
    '>=': '<',
    '<=': '>',
  };
  return `${match[1].trim()} ${inverse[match[2]]} ${match[3].trim()}`;
}
