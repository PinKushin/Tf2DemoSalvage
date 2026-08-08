// Dumps every entity update, snapshot by snapshot, as the reference parser reads them.
//
// Written for Tf2DemoSalvage's B13 investigation: its decoder runs 62 to 205 consecutive
// snapshots and then desynchronises, and a desynchronised reader reports impossible values
// rather than failing at the point it went wrong. Comparing this stream against its own
// locates the *first* divergence, which is the only one that matters.
//
// Output: "<snapshot>\t<entity>\t<update-type>\t<class>\t<prop-index>,<prop-index>,..."

use main_error::MainError;
use std::env;
use std::fs;
use tf_demo_parser::demo::data::DemoTick;
use tf_demo_parser::demo::message::Message;
use tf_demo_parser::demo::parser::MessageHandler;
use tf_demo_parser::MessageType;
pub use tf_demo_parser::{Demo, DemoParser, ParserState};

fn main() -> Result<(), MainError> {
    let args: Vec<_> = env::args().collect();
    if args.len() < 2 {
        println!("usage: snapshots <demo> [limit]");
        return Ok(());
    }

    let limit: usize = args
        .get(2)
        .and_then(|value| value.parse().ok())
        .unwrap_or(usize::MAX);

    let file = fs::read(args[1].clone())?;
    let demo = Demo::new(&file);
    let parser = DemoParser::new_with_analyser(demo.get_stream(), SnapshotDumper::new(limit));
    let (_, lines) = parser.parse()?;

    for line in lines {
        println!("{line}");
    }

    Ok(())
}

struct SnapshotDumper {
    limit: usize,
    snapshot: usize,
    lines: Vec<String>,
}

impl SnapshotDumper {
    fn new(limit: usize) -> Self {
        SnapshotDumper {
            limit,
            snapshot: 0,
            lines: Vec::new(),
        }
    }
}

impl MessageHandler for SnapshotDumper {
    type Output = Vec<String>;

    fn does_handle(message_type: MessageType) -> bool {
        matches!(message_type, MessageType::PacketEntities)
    }

    fn handle_message(&mut self, message: &Message, _tick: DemoTick, _state: &ParserState) {
        if let Message::PacketEntities(message) = message {
            if self.snapshot >= self.limit {
                return;
            }

            for entity in &message.entities {
                let props: Vec<String> =
                    entity.props.iter().map(|p| p.index.to_string()).collect();

                self.lines.push(format!(
                    "{}\t{}\t{:?}\t{}\t{}",
                    self.snapshot,
                    u32::from(entity.entity_index),
                    entity.update_type,
                    u16::from(entity.server_class),
                    props.join(",")
                ));
            }

            self.snapshot += 1;
        }
    }

    fn into_output(self, _state: &ParserState) -> Self::Output {
        self.lines
    }
}
