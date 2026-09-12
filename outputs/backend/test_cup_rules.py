"""Deterministic observations-to-rules checks, not VLM accuracy tests."""
import unittest
from vision import evaluate
from cup_test_fixtures import observations


class CupRuleTests(unittest.TestCase):
    def test_correct_and_future_layers(self):
        out = observations()
        self.assertEqual(evaluate(out, 2)[0], 'correct')
        out.cups = out.cups[:3]
        self.assertEqual(evaluate(out, 0)[0], 'correct')
        self.assertEqual(evaluate(out, 1)[0], 'incorrect')  # visible empty required middle
        out.layer_visibility.middle = 'unknown'
        self.assertEqual(evaluate(out, 1)[0], 'uncertain')

    def test_step1_no_top_needed(self):
        out = observations(); out.cups.pop()
        self.assertEqual(evaluate(out, 1)[0], 'correct')
        self.assertEqual(evaluate(out, 2)[0], 'incorrect')

    def test_each_cup_wrong_unknown_or_missing(self):
        for i in range(6):
            for alteration, expected in [('wrong','incorrect'),('unknown','uncertain'),('missing','incorrect')]:
                out = observations()
                if alteration == 'wrong': out.cups[i].opening = 'up' if out.cups[i].opening == 'down' else 'down'
                if alteration == 'unknown': out.cups[i].opening = 'unknown'
                if alteration == 'missing': out.cups.pop(i)
                self.assertEqual(evaluate(out, 2)[0], expected, (i,alteration))

    def test_hidden_missing_cup_uncertain(self):
        out=observations();out.cups.pop(0);out.layer_visibility.bottom='partial'
        self.assertEqual(evaluate(out,2)[0],'uncertain')

    def test_unassigned_cup_is_not_assumed_missing(self):
        out=observations();out.cups[0].layer='unknown'
        self.assertEqual(evaluate(out,2)[0],'uncertain')

    def test_supports_are_compared(self):
        for i in range(3,6):
            out=observations();out.cups[i].supported_by=['table']
            self.assertEqual(evaluate(out,2)[0],'incorrect')
            out.cups[i].supported_by=['unknown']
            self.assertEqual(evaluate(out,2)[0],'uncertain')

    def test_ambiguous_slots_and_layer(self):
        for field,value in [('position','unknown'),('layer','unknown'),('position','center')]:
            out=observations();setattr(out.cups[0],field,value)
            self.assertNotEqual(evaluate(out,2)[0],'correct')

    def test_visible_error_with_unknown_elsewhere(self):
        out=observations();out.cups[0].opening='up';out.cups[-1].opening='unknown'
        self.assertEqual(evaluate(out,2)[0],'incorrect')

    def test_order_and_future_errors_ignored(self):
        out=observations();out.cups[-1].opening='up';out.cups.reverse()
        self.assertEqual(evaluate(out,1)[0],'correct')
        self.assertEqual(evaluate(out,2)[0],'incorrect')


if __name__ == '__main__': unittest.main()
