### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
SSA001 | StringSyntaxAttribute.Usage | Warning | StringSyntax format mismatch between source and target
SSA002 | StringSyntaxAttribute.Usage | Warning | Source has no StringSyntax while target requires one
SSA003 | StringSyntaxAttribute.Usage | Warning | Source has StringSyntax while target has none
SSA004 | StringSyntaxAttribute.Usage | Warning | Equality comparison between mismatched StringSyntax values
SSA005 | StringSyntaxAttribute.Usage | Warning | Equality comparison with a value that lacks a StringSyntax attribute
SSA006 | StringSyntaxAttribute.Usage | Warning | UnionSyntax with a single option should be StringSyntax
SSA007 | StringSyntaxAttribute.Usage | Warning | StringSyntax can be replaced with a shortcut attribute
