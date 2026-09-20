import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useRefetchInterval } from "./hooks";

export interface EnrichedPortMapping {
  internalPort: number;
  externalPort: number;
  protocol: string;
  description: string;
  leaseSeconds: number;
  expiryUtc: string | null;
  isActive: boolean;
  status: string;
  errorMessage?: string | null;
}

export interface PortMappingStatus {
  protocol: string;
  gatewayIp: string;
  externalIp: string;
  routerModel: string;
  mappings: EnrichedPortMapping[];
  lastRenewalUtc: string | null;
  nextRenewalUtc: string | null;
}

export async function getPortMappingStatus(): Promise<PortMappingStatus> {
  return apiClient.get<PortMappingStatus>("/portmapping");
}

export async function refreshPortMapping(): Promise<PortMappingStatus> {
  return apiClient.post<PortMappingStatus>("/portmapping/refresh");
}

export function usePortMappingStatus() {
  const interval = useRefetchInterval();
  return useQuery<PortMappingStatus>({
    queryKey: ["portmapping", "status"],
    queryFn: getPortMappingStatus,
    refetchInterval: interval,
  });
}

export function useRefreshPortMapping() {
  const queryClient = useQueryClient();
  return useMutation<PortMappingStatus, Error, void>({
    mutationFn: refreshPortMapping,
    onSuccess: (data) => {
      queryClient.setQueryData(["portmapping", "status"], data);
      queryClient.invalidateQueries({ queryKey: ["network"] });
    },
  });
}
