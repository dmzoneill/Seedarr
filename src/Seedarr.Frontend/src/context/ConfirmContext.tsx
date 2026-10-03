import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
} from "react";
import { ConfirmModal } from "../components/ConfirmModal";
import ErrorBoundary from "../components/ErrorBoundary";

export interface ConfirmOptions {
  title?: string;
  message: string | React.ReactNode;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
}

export type ConfirmDialogFn = (
  options: ConfirmOptions | string,
) => Promise<boolean>;

interface ConfirmContextType {
  confirm: ConfirmDialogFn;
}

interface QueueItem {
  options: ConfirmOptions;
  resolve: (value: boolean) => void;
}

export interface ConfirmModalState {
  isOpen: boolean;
  options: ConfirmOptions;
}

export interface ConfirmQueueHandler {
  confirm: (options: ConfirmOptions | string) => Promise<boolean>;
  handleConfirm: () => void;
  handleCancel: () => void;
  processNext: (confirmed: boolean) => void;
  getState: () => ConfirmModalState;
  getQueueLength: () => number;
}

export function createConfirmQueue(
  onStateChange: (state: ConfirmModalState) => void = () => {},
): ConfirmQueueHandler {
  let modalState: ConfirmModalState = {
    isOpen: false,
    options: { message: "" },
  };
  const queue: QueueItem[] = [];
  let activeItem: QueueItem | null = null;

  const setState = (nextState: ConfirmModalState) => {
    modalState = nextState;
    onStateChange(nextState);
  };

  const confirm = (options: ConfirmOptions | string): Promise<boolean> => {
    const parsedOptions: ConfirmOptions = parseConfirmOptions(options);

    return new Promise<boolean>((resolve) => {
      const item: QueueItem = { options: parsedOptions, resolve };
      if (!activeItem) {
        activeItem = item;
        setState({
          isOpen: true,
          options: parsedOptions,
        });
      } else {
        queue.push(item);
      }
    });
  };

  const processNext = (confirmed: boolean) => {
    const current = activeItem;
    if (current) {
      current.resolve(confirmed);
      activeItem = null;
    }

    const nextItem = queue.shift();
    if (nextItem) {
      activeItem = nextItem;
      setState({
        isOpen: true,
        options: nextItem.options,
      });
    } else {
      setState({
        isOpen: false,
        options: { message: "" },
      });
    }
  };

  return {
    confirm,
    handleConfirm: () => processNext(true),
    handleCancel: () => processNext(false),
    processNext,
    getState: () => modalState,
    getQueueLength: () => queue.length,
  };
}

const ConfirmContext = createContext<ConfirmContextType | undefined>(undefined);

export function parseConfirmOptions(options: ConfirmOptions | string): ConfirmOptions {
  return typeof options === "string" ? { message: options } : options;
}

export const ConfirmProvider: React.FC<{ children: React.ReactNode }> = ({
  children,
}) => {
  const [modalState, setModalState] = useState<ConfirmModalState>({
    isOpen: false,
    options: { message: "" },
  });

  const handlerRef = useRef<ConfirmQueueHandler | null>(null);
  if (!handlerRef.current) {
    handlerRef.current = createConfirmQueue(setModalState);
  }
  const handler = handlerRef.current;

  const confirm: ConfirmDialogFn = useCallback((options) => {
    return handler ? handler.confirm(options) : Promise.resolve(false);
  }, [handler]);

  const handleConfirm = useCallback(() => {
    handler?.handleConfirm();
  }, [handler]);

  const handleCancel = useCallback(() => {
    handler?.handleCancel();
  }, [handler]);

  return (
    <ConfirmContext.Provider value={{ confirm }}>
      {children}
      <ErrorBoundary>
        <ConfirmModal
          isOpen={modalState.isOpen}
          title={modalState.options.title}
          message={modalState.options.message}
          confirmText={modalState.options.confirmText}
          cancelText={modalState.options.cancelText}
          danger={modalState.options.danger}
          onConfirm={handleConfirm}
          onCancel={handleCancel}
        />
      </ErrorBoundary>
    </ConfirmContext.Provider>
  );
};

export function useConfirm(): ConfirmDialogFn {
  const context = useContext(ConfirmContext);
  if (!context) {
    throw new Error("useConfirm must be used within a ConfirmProvider");
  }
  return context.confirm;
}

export function useConfirmDialog(): ConfirmDialogFn {
  return useConfirm();
}

export default ConfirmContext;
