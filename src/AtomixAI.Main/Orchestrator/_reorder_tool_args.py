"""One-shot: reorder few-shot tool argument keys to thought → sequence → active_context_tag → user_facing_message."""
import json
import pathlib
import re

path = pathlib.Path(__file__).with_name("instructions.py")
text = path.read_text(encoding="utf-8")
order = ["thought", "sequence", "active_context_tag", "user_facing_message"]
pat = re.compile(r'("arguments": ")((?:\\.|[^"\\])*)(")')


def unescape_py_string(body: str) -> str:
    # body is JSON-as-Python-string content with \" escapes; keep UTF-8 chars intact
    return json.loads('"' + body + '"')


def escape_py_string(s: str) -> str:
    return json.dumps(s, ensure_ascii=False)[1:-1]


def normalize_tag(tag: str) -> str:
    if tag == "Length":
        return "create_wall:Length"
    if tag == "Categories":
        return "filter_elements:Categories"
    return tag


count = 0


def repl(m: re.Match) -> str:
    global count
    body = m.group(2)
    obj = json.loads(unescape_py_string(body))
    if not isinstance(obj, dict) or "thought" not in obj:
        return m.group(0)
    if "active_context_tag" in obj:
        obj["active_context_tag"] = normalize_tag(obj.get("active_context_tag") or "")
    new_obj = {k: obj[k] for k in order if k in obj}
    for k, v in obj.items():
        if k not in new_obj:
            new_obj[k] = v
    dumped = json.dumps(new_obj, ensure_ascii=False, separators=(",", ": "))
    count += 1
    return m.group(1) + escape_py_string(dumped) + m.group(3)


new_text = pat.sub(repl, text)
path.write_text(new_text, encoding="utf-8")

# verify
text2 = path.read_text(encoding="utf-8")
for i, m in enumerate(pat.finditer(text2)):
    obj = json.loads(unescape_py_string(m.group(2)))
    print(i, list(obj.keys()), "tag=", repr(obj.get("active_context_tag")))
print("done", count)
