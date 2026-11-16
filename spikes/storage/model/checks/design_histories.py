"""Histories the design quotes from its model that no recorded output holds, replayed in the model
as it stands: written on the branch, not by the design's agent.

- Design 12.4: the six counterexamples that changed the design. Each was found against an earlier
  version of the model, which was not kept, so finding them cannot be repeated. What can be run is
  the design's statement that each "is closed": every history in the final model, as written and
  with each part of the read rule switched off in turn, through the model's own checks.
- Design 6.4 item 5: Seal again after a decision was lost in both carriers, without a trace and
  with records, which the design states and `results-full.txt` does not hold.

The histories are written in the model's own steps, with the design's words mapped to its
selectors; where the design leaves a selector open ("the head", "the older head") the mapping is
stated beside the history. Output: `spikes\\storage\\model\\out\\design-histories.txt`.

    uv run --no-project python -I -X utf8 spikes\\storage\\model\\checks\\design_histories.py
"""
import os
import sys

MODEL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, MODEL)
import crdt_model as M  # noqa: E402

SWITCHES = [('as written', M.FULL), ('no snapshot', M.Flags(snapshot=False)), ('no cover', M.Flags(cover=False)),
            ('no witness', M.Flags(T=False)), ('no record rule', M.Flags(recrule=False)),
            ('no head tag', M.Flags(headtag=False)), ('no decision tag', M.Flags(dtag=False))]

HISTORIES = [
    ('12.4 item 1, a decision that arrives without its head; "the head" is the newest', 2,
     [('prem', 0, 'pf'), ('del', 0, ('head', 'new'), 1), ('merge', 'xc', 1, 0), ('del', 1, ('prem',), 1)]),
    ('12.4 item 2, a deleted head as high as the one in force; "the head" is the newest', 2,
     [('rule', 0, 'root', 'none'), ('prem', 1, 'pf'), ('del', 0, ('ver', 'rule:root', 'new'), 1),
      ('del', 0, ('head', 'new'), 1), ('merge', 'kp_sync', 1, 0)]),
    ('12.4 item 3, a deleted head lower than the one in force; "the older head" is the lowest', 2,
     [('rule', 0, 'root', 'r'), ('rule', 1, 'root', 'rw'), ('del', 0, ('ver', 'rule:root', 'new'), 1),
      ('rule', 1, 'N', 'r'), ('merge', 'kp_sync', 1, 0), ('del', 1, ('head', 'old'), 1)]),
    ('12.4 item 4, a head edited by hand; "the head" is the newest', 2,
     [('renew', 1, 'c0', 'forever'), ('del', 1, ('ver', 'renew:c0', 'new'), 0), ('edit', 1, ('head', 'new')),
      ('merge', 'k2a', 1, 0)]),
    ('12.4 item 5, a decision edited by hand after its head was lost; "the head" is the newest', 3,
     [('renew', 2, 'c0', 'forever'), ('rule', 0, 'root', 'none'), ('del', 2, ('head', 'new'), 0),
      ('merge', 'k2a', 2, 0), ('edit', 2, ('ver', 'renew:c0', 'new'))]),
    ('12.4 item 6, a revocation undone by a deletion that left a record; "the head" is the newest', 2,
     [('revoke', 1, 'c0'), ('del', 1, ('head', 'new'), 1), ('merge', 'kp_other', 0, 1),
      ('del', 0, ('ref', 'revoked'), 1)]),
    ('6.4 item 5, Seal again after both carriers of a standing answer went without a trace', 2,
     [('renew', 0, 'c0', 'forever'), ('del', 0, ('head', 'new'), 0), ('del', 0, ('ver', 'renew:c0', 'new'), 0),
      ('reseal', 0)]),
    ('6.4 item 5, the same with deletion records', 2,
     [('renew', 0, 'c0', 'forever'), ('del', 0, ('head', 'new'), 1), ('del', 0, ('ver', 'renew:c0', 'new'), 1),
      ('reseal', 0)]),
]


def main():
    lines = []
    for name, nrep, ops in HISTORIES:
        lines.append('## ' + name)
        lines.append('   ' + '; '.join(' '.join(str(x) for x in op) for op in ops))
        for label, flags in SWITCHES:
            fails = M.run(ops, nrep, flags)
            lines.append('   %-16s %s' % (label, '; '.join('%s at step %d, %s' % (p, s + 1, d) for p, s, d in fails)
                                         or 'no failure'))
        lines.append('')
    out = os.path.join(MODEL, 'out')
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'design-histories.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines))
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
