import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';

interface ScheduledTask {
  typeName: string;
  interval: number;
  lastExecution: string | null;
  lastStartTime: string | null;
}

function SystemTasks() {
  const { data: tasks, isLoading } = useQuery<ScheduledTask[]>({
    queryKey: ['system', 'tasks'],
    queryFn: () => apiClient.get('/system/task'),
    retry: false,
  });

  return (
    <div>
      <h1 className="page-heading">Scheduled Tasks</h1>
      {isLoading && <p className="loading">Loading tasks...</p>}
      {tasks && (
        <div className="card">
          {tasks.map((task) => (
            <div key={task.typeName} className="status-row">
              <span className="status-label">{task.typeName}</span>
              <span className="status-value">
                Every {task.interval}min
                {task.lastExecution && ` | Last: ${new Date(task.lastExecution).toLocaleString()}`}
              </span>
            </div>
          ))}
          {tasks.length === 0 && <p className="loading">No scheduled tasks.</p>}
        </div>
      )}
    </div>
  );
}

export default SystemTasks;
