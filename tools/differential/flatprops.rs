// Dumps each server class's flattened property list, in the order entity deltas index it.
//
// Written for Tf2DemoSalvage's differential harness: its own flattener produces a list per
// class, and a disagreement in order or length is invisible in normal output because a wrong
// index reads a real value into the wrong field.
//
// Output: one line per property, "<class-id>\t<class-name>\t<index>\t<table>.<prop>".

use fnv::FnvHashMap;
use main_error::MainError;
use std::env;
use std::fs;
use tf_demo_parser::demo::data::DemoTick;
use tf_demo_parser::demo::message::Message;
use tf_demo_parser::demo::packet::datatable::{ParseSendTable, SendTableName, ServerClass};
use tf_demo_parser::demo::parser::MessageHandler;
use tf_demo_parser::demo::sendprop::{SendPropIdentifier, SendPropName};
use tf_demo_parser::MessageType;
pub use tf_demo_parser::{Demo, DemoParser, ParserState};

fn main() -> Result<(), MainError> {
    let args: Vec<_> = env::args().collect();
    if args.len() < 2 {
        println!("usage: flatprops <demo> [class-name]");
        return Ok(());
    }

    let wanted = args.get(2).cloned();
    let file = fs::read(args[1].clone())?;
    let demo = Demo::new(&file);
    let parser = DemoParser::new_with_analyser(demo.get_stream(), FlatAnalyzer::default());
    let (_, lines) = parser.parse()?;

    for line in lines {
        match &wanted {
            Some(class) if !line.contains(&format!("\t{class}\t")) => {}
            _ => println!("{line}"),
        }
    }

    Ok(())
}

#[derive(Default)]
struct FlatAnalyzer {
    prop_names: FnvHashMap<SendPropIdentifier, (SendTableName, SendPropName)>,
}

impl MessageHandler for FlatAnalyzer {
    type Output = Vec<String>;

    fn does_handle(message_type: MessageType) -> bool {
        matches!(message_type, MessageType::PacketEntities)
    }

    fn handle_message(&mut self, _message: &Message, _tick: DemoTick, _state: &ParserState) {}

    fn handle_data_tables(
        &mut self,
        parse_tables: &[ParseSendTable],
        _server_classes: &[ServerClass],
        _parser_state: &ParserState,
    ) {
        for table in parse_tables {
            for prop_def in &table.props {
                self.prop_names.insert(
                    prop_def.identifier(),
                    (table.name.clone(), prop_def.name.clone()),
                );
            }
        }
    }

    fn into_output(self, state: &ParserState) -> Self::Output {
        let mut lines = Vec::new();

        for class in &state.server_classes {
            let table = match state.send_tables.get(usize::from(class.id)) {
                Some(table) => table,
                None => continue,
            };

            for (index, prop) in table.flattened_props.iter().enumerate() {
                let name = match self.prop_names.get(&prop.identifier) {
                    Some((table_name, prop_name)) => format!("{table_name}.{prop_name}"),
                    None => format!("<unknown {:?}>", prop.identifier),
                };

                lines.push(format!(
                    "{}\t{}\t{}\t{}",
                    u16::from(class.id),
                    class.name,
                    index,
                    name
                ));
            }
        }

        lines
    }
}
