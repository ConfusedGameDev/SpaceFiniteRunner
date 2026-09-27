"""A small parser for Unity's serialized YAML (one MonoBehaviour document per asset).
Values stay strings; maps become dicts and lists become lists."""
import re

KEY = re.compile(r"^[\w\-]+:(\s|$)")


def parse_yaml(text):
    lines = [l.rstrip() for l in text.split("\n") if l.strip() and not l.startswith(("%", "---"))]

    def ind(k):
        return len(lines[k]) - len(lines[k].lstrip(" "))

    def is_item(k):
        return lines[k].strip().startswith("- ") or lines[k].strip() == "-"

    def parse_list(idx, indent):
        items = []
        while idx < len(lines) and ind(idx) == indent and is_item(idx):
            item = lines[idx].strip()[2:]
            idx += 1
            if KEY.match(item):
                k, _, v = item.partition(":")
                sub = {}
                v = v.strip()
                if v:
                    sub[k.strip()] = v
                else:
                    sub[k.strip()], idx = nested(idx, indent + 2)
                if idx < len(lines) and ind(idx) == indent + 2 and not is_item(idx):
                    more, idx = parse_map(idx, indent + 2)
                    sub.update(more)
                items.append(sub)
            else:
                items.append(item)
            while idx < len(lines) and ind(idx) > indent + 2:
                idx += 1
        return items, idx

    def nested(idx, child_indent):
        """The value of a key whose line ended in ':' — a list at the key's indent or deeper, or a map deeper."""
        if idx >= len(lines):
            return "", idx
        if is_item(idx) and ind(idx) >= child_indent - 2:
            return parse_list(idx, ind(idx))
        if ind(idx) >= child_indent:
            return parse_map(idx, ind(idx))
        return "", idx

    def parse_map(idx, indent):
        result = {}
        while idx < len(lines) and ind(idx) == indent and not is_item(idx):
            k, _, v = lines[idx].strip().partition(":")
            v = v.strip()
            idx += 1
            if v:
                while idx < len(lines) and ind(idx) > indent and v.count("{") > v.count("}"):
                    v += " " + lines[idx].strip()
                    idx += 1
                result[k] = v
            else:
                result[k], idx = nested(idx, indent + 2)
            while idx < len(lines) and ind(idx) > indent:
                idx += 1
        return result, idx

    root, _ = parse_map(0, 0)
    mb = root.get("MonoBehaviour", root)
    return mb if isinstance(mb, dict) else {}
