import tempfile
import unittest
from pathlib import Path
from art_change_history import update_history


class HistoryTests(unittest.TestCase):
    def test_add_update_remove_and_noop(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'history.json'
            r=dict(id='unstable-1',category='equipment',name='Key',candidates=[dict(assetId='a',sha256='1')])
            self.assertEqual('added',update_history(path,[r])[0]['events'][0]['kind'])
            r['id']='unstable-2'
            self.assertEqual(1,len(update_history(path,[r])))
            r['candidates']=[dict(assetId='b',sha256='2')]
            event=update_history(path,[r])[0]['events'][0]
            self.assertEqual(('updated',['b'],['a']),(event['kind'],event['added'],event['removed']))
            self.assertEqual('removed',update_history(path,[])[0]['events'][0]['kind'])
            self.assertEqual(3,len(update_history(path,[])))


if __name__=='__main__':unittest.main()
