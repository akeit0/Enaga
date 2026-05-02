declare module "react-refresh/runtime" {
  type RefreshSignature = (
    type: unknown,
    key?: string,
    forceReset?: boolean,
    getCustomHooks?: () => unknown[],
  ) => unknown;

  interface RefreshUpdate {
    updatedFamilies: Iterable<object>;
    staleFamilies: Iterable<object>;
  }

  interface RefreshRuntimeApi {
    injectIntoGlobalHook(globalObject: object): void;
    register(type: unknown, id: string): void;
    getFamilyByID?(id: string): object | undefined;
    createSignatureFunctionForTransform(): RefreshSignature;
    performReactRefresh(): RefreshUpdate | null;
  }

  const RefreshRuntime: RefreshRuntimeApi;
  export default RefreshRuntime;
}
