enum SyntaxState
{
    Unknown,
    NotPresent,
    Present,

    // An explicit `[StringSyntax("*")]` wildcard: the slot accepts a value of any
    // syntax. Distinct from Unknown (which is "we can't tell") — Any is authored
    // intent. Like Unknown it suppresses SSA001/SSA002/SSA003/SSA004/SSA005 on its
    // side, but it must NOT be modelled as a Present tag with value "*", or a bare
    // (NotPresent) source flowing into an Any target would spuriously fire SSA002.
    Any
}