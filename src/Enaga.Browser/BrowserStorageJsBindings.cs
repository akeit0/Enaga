using Okojo;
using Okojo.Objects;
using Okojo.Runtime;

namespace Enaga.Browser;

internal static class BrowserStorageJsBindings
{
    private static readonly JsShapePropertyFlags OpenFlags = JsShapePropertyFlags.Open;

    public static JsPlainObject CreateStorageObject(JsRealm realm, BrowserStorageArea storage)
    {
        var obj = new JsPlainObject(realm);
        obj.DefineAccessorProperty(
            "length",
            new JsHostFunction(realm, (in CallInfo _) => JsValue.FromInt32(storage.Length), "get length", 0),
            null,
            OpenFlags);
        obj.DefineDataProperty("key", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var index = info.GetArgumentOrDefault(0, JsValue.Undefined).IsNumber
                ? (int)info.GetArgumentOrDefault(0, JsValue.Undefined).NumberValue
                : -1;
            return storage.Key(index) is { } key ? JsValue.FromString(key) : JsValue.Null;
        }, "key", 1)), OpenFlags);
        obj.DefineDataProperty("getItem", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var key = JsValueToStorageString(info.GetArgumentOrDefault(0, JsValue.Undefined));
            return storage.GetItem(key) is { } value ? JsValue.FromString(value) : JsValue.Null;
        }, "getItem", 1)), OpenFlags);
        obj.DefineDataProperty("setItem", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var key = JsValueToStorageString(info.GetArgumentOrDefault(0, JsValue.Undefined));
            var value = JsValueToStorageString(info.GetArgumentOrDefault(1, JsValue.Undefined));
            storage.SetItem(key, value);
            return JsValue.Undefined;
        }, "setItem", 2)), OpenFlags);
        obj.DefineDataProperty("removeItem", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var key = JsValueToStorageString(info.GetArgumentOrDefault(0, JsValue.Undefined));
            storage.RemoveItem(key);
            return JsValue.Undefined;
        }, "removeItem", 1)), OpenFlags);
        obj.DefineDataProperty("clear", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo _) =>
        {
            storage.Clear();
            return JsValue.Undefined;
        }, "clear", 0)), OpenFlags);
        return obj;
    }

    private static string JsValueToStorageString(JsValue value)
    {
        if (value.IsString)
            return value.AsString();
        if (value.IsNull)
            return "null";
        if (value.IsUndefined)
            return "undefined";
        return value.ToString();
    }
}
