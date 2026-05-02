export type CatalogTabId = "overview" | "minimum" | "inputs" | "rendering" | "gradients" | "shaders" | "animation" | "components" | "communication";

export type CatalogTabDefinition = {
  id: CatalogTabId;
  label: string;
  subtitle: string;
};
