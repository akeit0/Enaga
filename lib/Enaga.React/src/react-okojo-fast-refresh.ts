import RefreshRuntime from "react-refresh/runtime";
type NativeFastRefreshGlobal = typeof globalThis & {
  __nativeFastRefreshEnabled?: boolean;
  __nativeRefreshRuntimeInstalled?: boolean;
  __nativeFastRefreshEpoch?: number;
  __nativeRefreshFamilyIds?: WeakMap<object, string>;
  __nativeCreateRefreshRegister?: (moduleId: string) => (type: unknown, exportName: string) => void;
  __nativeCreateRefreshSignature?: () => ReturnType<typeof RefreshRuntime.createSignatureFunctionForTransform>;
  __nativeCommitFastRefresh?: () => void;
};

const refreshGlobal = globalThis as NativeFastRefreshGlobal;

function installNativeFastRefresh(): void {
  if (process.env.NODE_ENV !== "development") {
    return;
  }

  if (refreshGlobal.__nativeRefreshRuntimeInstalled !== true) {
    RefreshRuntime.injectIntoGlobalHook(refreshGlobal);
    refreshGlobal.__nativeRefreshRuntimeInstalled = true;
  }

  refreshGlobal.__nativeFastRefreshEnabled = true;
  refreshGlobal.__nativeFastRefreshEpoch = (refreshGlobal.__nativeFastRefreshEpoch ?? 0) + 1;
  refreshGlobal.__nativeRefreshFamilyIds ??= new WeakMap<object, string>();
  refreshGlobal.__nativeCreateRefreshRegister = (moduleId) => (type, exportName) => {
    const familyId = `${moduleId} ${exportName}`;
    RefreshRuntime.register(type as never, familyId);
    if ((typeof type === "function" || (typeof type === "object" && type !== null))) {
      const family = RefreshRuntime.getFamilyByID?.(familyId);
      if (family && typeof family === "object") {
        refreshGlobal.__nativeRefreshFamilyIds?.set(family as object, familyId);
      }
    }
  };
  refreshGlobal.__nativeCreateRefreshSignature = () => RefreshRuntime.createSignatureFunctionForTransform();
  refreshGlobal.__nativeCommitFastRefresh = () => {
    const update = RefreshRuntime.performReactRefresh();
    if (!update) {
      return;
    }

    const familyIds = refreshGlobal.__nativeRefreshFamilyIds;
    const updated = Array.from(update.updatedFamilies, (family) => familyIds?.get(family as object) ?? "<unknown>");
    const stale = Array.from(update.staleFamilies, (family) => familyIds?.get(family as object) ?? "<unknown>");
    console.log(
      `[fast-refresh] updated=${updated.length} stale=${stale.length}`,
      { updated, stale },
    );
  };
}

installNativeFastRefresh();

export function commitNativeFastRefresh(): void {
  if (refreshGlobal.__nativeFastRefreshEnabled === true) {
    refreshGlobal.__nativeCommitFastRefresh?.();
  }
}
